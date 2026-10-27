// End-to-end test of the web build: two Chrome windows with fake cameras and microphones join the same room over
// the real signalling network, find each other, stand face to face, and must each see the other's camera on their
// avatar's face screen and receive their voice. Then one writes a sticky note in the page's editor and sticks it on a
// noticeboard, the other sees it and moves it, and a third browser arriving later finds it where it was moved to.
// Screenshots land in Screenshots/web_*.png.
//
//   cd Tools/web && npm install && npm run e2e            (needs a web build in Builds/WebGL and Google Chrome)
//   npm run e2e -- --headed                                (watch it happen)
//   npm run e2e -- --url https://you.github.io/facecast/   (test a deployed copy instead of a local build)
//
// Chrome's fake camera shows a moving test pattern; the two windows run in separate profiles, so it's a real
// peer-to-peer connection between two browsers, just on one machine.

import {chromium} from 'playwright-core'
import {spawn} from 'node:child_process'
import {fileURLToPath} from 'node:url'
import path from 'node:path'
import fs from 'node:fs'

const here = path.dirname(fileURLToPath(import.meta.url))
const root = path.resolve(here, '../..')
const args = process.argv.slice(2)
const headed = args.includes('--headed')
const urlArg = args.includes('--url') ? args[args.indexOf('--url') + 1] : null
const chrome = process.env.CHROME || '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'
const port = 8791
const shots = path.join(root, 'Screenshots')
const room = `e2e-${Math.random().toString(36).slice(2, 8)}`

const sleep = ms => new Promise(r => setTimeout(r, ms))
const log = (...a) => console.log('[e2e]', ...a)

async function until(what, fn, seconds) {
  const end = Date.now() + seconds * 1000
  let last
  while (Date.now() < end) {
    last = await fn()
    if (last) return last
    await sleep(500)
  }
  throw new Error(`timed out after ${seconds}s waiting for ${what}`)
}

let server = null
let base = urlArg
if (!base) {
  server = spawn('python3', [path.join(root, 'Tools/serve_web.py'), '--port', String(port)], {stdio: ['ignore', 'pipe', 'inherit']})
  await new Promise((resolve, reject) => {
    server.stdout.on('data', d => { if (String(d).includes('Serving')) resolve() })
    server.on('exit', code => reject(new Error(`server exited (${code})`)))
  })
  base = `http://localhost:${port}/`
}

const browserArgs = [
  '--use-fake-ui-for-media-stream',      // allow camera and microphone without asking
  '--use-fake-device-for-media-stream',  // a test pattern and a beeping tone instead of real devices
  '--autoplay-policy=no-user-gesture-required',
  '--ignore-gpu-blocklist',
  '--enable-unsafe-swiftshader',
]
const failures = []
const browsers = []

// Separate browsers: separate profiles, separate WebRTC stacks.
async function open(name) {
  const browser = await chromium.launch({executablePath: chrome, headless: !headed, args: browserArgs})
  browsers.push(browser)
  const page = await browser.newPage({viewport: {width: 1280, height: 720}})
  page.on('console', m => {
    const text = m.text()
    if (m.type() === 'error' || /\[(Room|App|facecast|Noticeboards|NoteHand)\]|Exception|WebGL: INVALID/.test(text)) log(`${name}:`, text.slice(0, 300))
    if (/Exception|WebGL: INVALID|INVALID_OPERATION/.test(text)) failures.push(`${name}: ${text.slice(0, 200)}`)
  })
  page.on('pageerror', e => { log(`${name} page error:`, e.message); failures.push(`${name}: ${e.message}`) })
  return {name, page}
}

// The note saying `text` on the first board, as the game in `page` has it.
const noteOnBoard = (page, text) => page.evaluate(t => (window.facecast.debug.notes()?.boards[0].notes || []).find(n => n.text === t) || null, text)
const near = (n, x, y) => n && Math.abs(n.x - x) < 0.02 && Math.abs(n.y - y) < 0.02

try {
  const pages = [await open('Ana'), await open('Ben')]
  const [a, b] = pages

  // Ana goes through the join card like a person: types her name, picks coral, presses Join.
  await a.page.goto(`${base}#${room}`)
  await until('the game to load', () => a.page.evaluate(() => !document.getElementById('join').disabled), 120)
  fs.mkdirSync(shots, {recursive: true})
  await a.page.screenshot({path: path.join(shots, 'web_card.png')})
  await a.page.click('#name')
  await a.page.keyboard.press('ControlOrMeta+A')
  await a.page.keyboard.type('Ana')  // real key events: the game must not swallow them
  const typed = await a.page.inputValue('#name')
  if (typed !== 'Ana') failures.push(`typing in the name field gave "${typed}"`)
  await a.page.click('[aria-label="#F2705A"]')
  await a.page.focus('#room')
  await a.page.keyboard.press('Enter')  // Enter in a field joins
  // Ben skips the card.
  await b.page.goto(`${base}?autojoin&name=Ben&color=4CC9A0#${room}`)
  log(`room ${room}; waiting for both to load and join…`)
  for (const {name, page} of pages)
    await until(`${name} to join`, () => page.evaluate(() => window.facecast?.debug.joined()), 120)

  log('joined; waiting for them to find each other and exchange video…')
  for (const {name, page} of pages) {
    const peer = await until(`${name} to see the other's camera on their face`, () => page.evaluate(() => {
      const p = window.facecast.debug.peers().find(p => p.hello)
      return p && p.video !== '0x0' && p.uploads > 0 ? p : null
    }), 90)
    log(`${name} sees ${peer.name}: video ${peer.video}, ${peer.uploads} frames copied to the face, audio ${peer.audio}`)
    const expected = name === 'Ana' ? 'Ben' : 'Ana'
    if (peer.name !== expected) failures.push(`${name} sees "${peer.name}", expected ${expected}`)
    if (!peer.audio) failures.push(`${name} got no audio track from ${peer.name}`)
  }

  // Face to face, a metre and a half apart, then look at each other.
  await a.page.evaluate(() => window.facecast.debug.unity('teleport 0 -6 0 4'))  // out of Ben's way
  await b.page.evaluate(() => window.facecast.debug.unity('teleport 0 -4.5 180 4'))
  await sleep(2500)

  // Voice: the fake microphone beeps; the level should rise above silence now and then.
  for (const {name, page} of pages) {
    const level = await page.evaluate(async () => {
      const id = window.facecast.debug.peers()[0].id
      let max = 0
      for (let i = 0; i < 30; i++) {
        max = Math.max(max, window.facecast.audioLevel(id))
        await new Promise(r => setTimeout(r, 100))
      }
      return max
    })
    log(`${name} hears the other at up to ${level.toFixed(2)}`)
    if (level <= 0.05) failures.push(`${name} heard nothing from the other`)
  }

  for (const {name, page} of pages) {
    const file = path.join(shots, `web_${name.toLowerCase()}.png`)
    await page.screenshot({path: file})
    const uploads = await page.evaluate(() => window.facecast.debug.uploads())
    log(`${name}: ${file}  (frames copied per feed: ${JSON.stringify(uploads)})`)
  }

  // Sticky notes. Ana walks up to the first noticeboard and takes a note from its blue notepad; the page's note
  // editor opens; she writes on it, makes it green, and sticks it on the board.
  log('notes: Ana writes a note and sticks it on the first board…')
  await a.page.evaluate(() => { window.facecast.debug.unity('note goto 0'); window.facecast.debug.unity('note take 2') })
  await until('the note editor to open', () => a.page.evaluate(() => window.facecast.debug.writing() && !document.getElementById('noteEditor').hidden), 10)
  await a.page.keyboard.type('Hello from Ana')  // the editor has the focus
  await a.page.click('[aria-label="Green"]')
  await sleep(400)  // the paper's colour fades over
  await a.page.screenshot({path: path.join(shots, 'web_note_editor.png')})
  await a.page.focus('#noteText')
  await a.page.keyboard.press('ControlOrMeta+Enter')
  const held = await until('Ana to hold her note', () => a.page.evaluate(() => window.facecast.debug.notes()?.held), 10)
  await a.page.evaluate(() => window.facecast.debug.unity('note stick 0 0.3 0.6'))
  const mine = await until("Ana's note on her board", () => noteOnBoard(a.page, 'Hello from Ana'), 10)
  if (mine.id !== held || mine.color !== 3 || mine.by !== 'Ana' || !near(mine, 0.3, 0.6))
    failures.push(`Ana's note came out wrong: ${JSON.stringify(mine)}`)
  await a.page.evaluate(() => window.facecast.debug.unity('teleport 0 -6 0 4'))  // out of Ben's way

  // Ben sees it arrive, then moves it.
  const seen = await until("Ana's note on Ben's board", () => noteOnBoard(b.page, 'Hello from Ana'), 20)
  if (!near(seen, 0.3, 0.6)) failures.push(`Ben has Ana's note at ${seen.x}, ${seen.y}`)
  log(`Ben sees "${seen.text}" by ${seen.by} on the first board; he moves it`)
  await sleep(1000)  // Ana's avatar walking off the spot, on Ben's screen
  await b.page.evaluate(id => {
    const unity = window.facecast.debug.unity
    unity('note goto 0')
    unity(`note pick ${id}`)
    unity('note stick 0 0.7 0.4')
  }, seen.id)
  await until('Ana to see it move', async () => near(await noteOnBoard(a.page, 'Hello from Ana'), 0.7, 0.4), 20)
  log('Ana sees it where Ben put it')
  await sleep(1500)
  await b.page.screenshot({path: path.join(shots, 'web_board.png')})

  // Someone arriving later gets the boards from the people already here.
  const c = await open('Cy')
  await c.page.goto(`${base}?autojoin&name=Cy&cam=0&mic=0#${room}`)
  await until('Cy to join', () => c.page.evaluate(() => window.facecast?.debug.joined()), 120)
  await until('Cy to get the note, where Ben moved it', async () => near(await noteOnBoard(c.page, 'Hello from Ana'), 0.7, 0.4), 60)
  log('Cy, arriving later, finds the note where Ben moved it')
} catch (e) {
  failures.push(e.message)
} finally {
  for (const b of browsers) await b.close().catch(() => {})
  if (server) server.kill()
}

if (failures.length) {
  log('FAILED:\n  ' + failures.join('\n  '))
  process.exit(1)
}
log('PASSED')

// End-to-end test of the web build: two Chrome windows with fake cameras and microphones join the same room over
// the real signalling network, find each other, stand face to face, and must each see the other's camera on their
// avatar's face screen and receive their voice. Clicking the game captures the mouse to look around, and once the
// browser lets it go (Esc), moving it must leave the view alone. The fake cameras show a portrait (fixtures/face.mjpeg), so the face
// zoom must find the face, zoom in on it and send the crop to the other. Then one writes a sticky note in the page's
// editor and sticks it on a noticeboard, and the other sees it and moves it; she posts a question in the question box,
// and he pulls it out and lays it on the table. He shares his screen (Chrome's fake screen): a projection goes up in
// front of him, and she must get its place and its picture; he makes it large, and hers must grow too. A third browser arriving later must find the note where
// it was moved to, the question on the table and his projection; when he stops sharing it comes down for everyone.
// Screenshots land in Screenshots/web_*.png.
//
//   cd Tools/web && npm install && npm run e2e            (needs a web build in Builds/WebGL and Google Chrome)
//   npm run e2e -- --headed                                (watch it happen)
//   npm run e2e -- --url https://you.github.io/facecast/   (test a deployed copy instead of a local build)
//   npm run e2e -- --relay                                 (everything through the site's TURN servers, as on strict networks)
//
// The windows run in separate profiles, so it's a real peer-to-peer connection between browsers, just on one machine.

import {chromium} from 'playwright-core'
import {spawn} from 'node:child_process'
import {fileURLToPath} from 'node:url'
import path from 'node:path'
import fs from 'node:fs'

const here = path.dirname(fileURLToPath(import.meta.url))
const root = path.resolve(here, '../..')
const args = process.argv.slice(2)
const headed = args.includes('--headed')
const relay = args.includes('--relay')
const q = relay ? 'relay&' : ''  // pages join with ?relay: their connections may only go through TURN
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
  '--use-fake-device-for-media-stream',  // a beeping tone instead of a real microphone...
  `--use-file-for-fake-video-capture=${path.join(here, 'fixtures/face.mjpeg')}`,  // ...and a portrait for a camera
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

// The note saying `text` on the first board (or the question in the box or on the table), as the game in `page` has it.
const noteOnBoard = (page, text) => page.evaluate(t => (window.facecast.debug.notes()?.boards[0].notes || []).find(n => n.text === t) || null, text)
const questionIn = (page, where, text) => page.evaluate(([w, t]) => (window.facecast.debug.notes()?.[w] || []).find(n => n.text === t) || null, [where, text])
const near = (n, x, y) => n && Math.abs(n.x - x) < 0.02 && Math.abs(n.y - y) < 0.02
const deltaAngle = (a, b) => ((a - b + 540) % 360) - 180
const fmt = c => c ? `x ${c.x.toFixed(2)} y ${c.y.toFixed(2)} w ${c.w.toFixed(2)} h ${c.h.toFixed(2)}` : 'none'
// Ana's face zoom, once it has settled on a face; Ben's copy of her crop.
const anaZoom = page => page.evaluate(() => window.facecast.debug.faceZoom())
const crop = async (page, zoomed) => page.evaluate(z => {
  const c = window.facecast.debug.peers().find(p => p.hello)?.crop
  return c && (z ? c.w < 0.8 : c.w > 0.97) ? c : null
}, zoomed)

try {
  const pages = [await open('Ana'), await open('Ben')]
  const [a, b] = pages

  // Ana goes through the join card like a person: types her name, picks coral, presses Join.
  await a.page.goto(`${base}${relay ? '?relay' : ''}#${room}`)
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
  await b.page.goto(`${base}?${q}autojoin&name=Ben&color=4CC9A0#${room}`)
  log(`room ${room}; waiting for both to load and join…`)
  for (const {name, page} of pages)
    await until(`${name} to join`, () => page.evaluate(() => window.facecast?.debug.joined()), 120)

  const turnInfo = await a.page.evaluate(() => window.facecast.debug.turn())
  log(`TURN: ${turnInfo.count} server(s)${turnInfo.error ? ` (turnUrl failed: ${turnInfo.error})` : ''}${relay ? ', relay only' : ''}`)
  if (relay && !turnInfo.count) failures.push('--relay, but the site has no TURN servers to relay through')
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

  // The mouse: a click on the game captures it, and moving it looks around. Once the browser lets go (Esc does this;
  // so does exitPointerLock), moving it must leave the view alone, and another click captures it again.
  const yawOf = page => page.evaluate(() => window.facecast.debug.pose()?.[3])
  const pointerLocked = page => page.evaluate(() => !!document.pointerLockElement)
  await a.page.mouse.click(640, 400, {delay: 120})  // a person's click: the browser grants the lock as it ends
  await until('a click to capture the mouse', () => pointerLocked(a.page), 5)
  let before = await yawOf(a.page)
  await a.page.mouse.move(820, 400, {steps: 12})
  await sleep(500)
  const turned = Math.abs(deltaAngle(await yawOf(a.page), before))
  if (turned < 3) failures.push(`moving the captured mouse turned Ana's view only ${turned.toFixed(1)}°`)
  await a.page.evaluate(() => document.exitPointerLock())
  await until('the mouse to be let go', async () => !(await pointerLocked(a.page)), 5)
  await sleep(300)
  before = await yawOf(a.page)
  await a.page.mouse.move(380, 420, {steps: 12})
  await sleep(600)
  const drift = Math.abs(deltaAngle(await yawOf(a.page), before))
  if (drift > 0.5) failures.push(`moving the free mouse still turned Ana's view, by ${drift.toFixed(1)}°`)
  await a.page.mouse.click(640, 400, {delay: 120})  // a person's click: the browser grants the lock as it ends
  await until('another click to capture the mouse again', () => pointerLocked(a.page), 5)
  await a.page.evaluate(() => document.exitPointerLock())
  await a.page.evaluate(() => window.facecast.debug.unity('teleport 0 -6 0 4'))
  log(`the mouse: captured, it turned the view ${turned.toFixed(0)}°; let go, moving it turned it ${drift.toFixed(1)}°`)

  // Face zoom: the portrait's face is left of centre, so the crop must zoom in and move left to frame it.
  for (const {name, page} of pages) {
    const z = await until(`${name}'s browser to find the face`, async () => {
      const z = await anaZoom(page)
      return z.state === 'ready' && z.faces > 0 && z.crop && z.crop.w < 0.8 ? z : z.state === 'failed' ? z : null
    }, 60)
    log(`${name}'s face zoom: ${z.state}, ${z.faces} face, crop ${fmt(z.crop)}`)
    if (z.state !== 'ready') failures.push(`${name}'s face detector didn't load`)
    const cx = z.crop.x + z.crop.w / 2
    if (cx < 0.25 || cx > 0.5) failures.push(`${name}'s crop isn't on the face (centre ${cx.toFixed(2)})`)
  }
  await sleep(2500)  // settled
  const sent = (await anaZoom(a.page)).crop
  const got = await until("Ben to get Ana's crop", () => crop(b.page, true), 10)
  log(`Ben shows Ana's face at ${fmt(got)}`)
  if (Math.abs(got.x - sent.x) > 0.03 || Math.abs(got.w - sent.w) > 0.03) failures.push(`Ben has Ana's crop as ${fmt(got)}, she sent ${fmt(sent)}`)
  // Ana switches the zoom off (her HUD button): Ben gets her whole picture; then back on.
  await a.page.click('#hudZoom')
  await until("Ben to see Ana's whole picture", () => crop(b.page, false), 10)
  await a.page.click('#hudZoom')
  await until("Ben to see Ana's face zoomed again", () => crop(b.page, true), 15)
  log('Ana turned her face zoom off and on again; Ben followed')
  await sleep(2500)

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

  // The question box: Ana writes a question on a card from the box's tray (the page's editor, as a card: no colours to
  // pick) and posts it; Ben pulls it out and lays it on the table; Ana sees it there.
  log('questions: Ana posts a question…')
  await a.page.evaluate(() => { window.facecast.debug.unity('note goto box'); window.facecast.debug.unity('note ask') })
  await until('the question card to open', () => a.page.evaluate(() => window.facecast.debug.asking()), 10)
  if (!(await a.page.evaluate(() => document.getElementById('noteColors').hidden))) failures.push('the question card offers colours')
  await a.page.keyboard.type("What's for lunch?")
  await sleep(400)  // the card fades in
  await a.page.screenshot({path: path.join(shots, 'web_question_editor.png')})
  await a.page.keyboard.press('ControlOrMeta+Enter')
  await until('Ana to hold her question', () => a.page.evaluate(() => window.facecast.debug.notes()?.held), 10)
  await a.page.evaluate(() => window.facecast.debug.unity('note post'))
  const posted = await until("the question in Ana's box", () => questionIn(a.page, 'box', "What's for lunch?"), 10)
  if (!posted.question || posted.by) failures.push(`Ana's question came out wrong: ${JSON.stringify(posted)}`)
  await a.page.evaluate(() => window.facecast.debug.unity('teleport 0 -6 0 4'))
  await until("the question in Ben's box", () => questionIn(b.page, 'box', "What's for lunch?"), 20)
  await sleep(1000)
  await b.page.evaluate(() => {
    const unity = window.facecast.debug.unity
    unity('note goto box')
    unity('note pull')
  })
  const pulled = await until('Ben to pull it out', () => b.page.evaluate(() => window.facecast.debug.notes()?.held), 10)
  if (pulled !== posted.id) failures.push(`Ben pulled ${pulled}, not Ana's question ${posted.id}`)
  await b.page.evaluate(() => window.facecast.debug.unity('note lay 0.62 0.5'))
  await until('Ana to see it on the table', () => questionIn(a.page, 'table', "What's for lunch?"), 20)
  if (await questionIn(a.page, 'box', "What's for lunch?")) failures.push("Ana's box still has the question")
  log('Ben pulled out the question and laid it on the table; Ana sees it there')
  await b.page.evaluate(() => window.facecast.debug.unity('note goto table'))
  await sleep(1500)
  await b.page.screenshot({path: path.join(shots, 'web_table.png')})

  // Sharing a screen: Ben presses the share button (Chrome's fake screen stands in for his); his projection goes up in
  // front of him; Ana gets its place and his screen's picture, and stands in front of it.
  log('screens: Ben shares his screen…')
  await b.page.evaluate(() => window.facecast.debug.unity('teleport 0 -11 0 0'))
  await sleep(600)
  await b.page.click('#hudShare')
  const shared = await until("Ben's projection to go up", () => b.page.evaluate(() => {
    const s = window.facecast.debug.shares().mine
    return s && s.at ? s : null
  }), 20)
  const [sx, , sz, syaw] = shared.at
  log(`Ben's projection is up at ${sx.toFixed(2)}, ${sz.toFixed(2)} facing ${syaw.toFixed(0)}°; his screen is ${shared.video}`)
  const ahead = sz + 11
  if (Math.abs(sx) > 0.05 || ahead < 1.5 || ahead > 2.85) failures.push(`Ben's projection isn't in front of him: ${shared.at}`)
  const screen = await until("Ana to get Ben's screen on his projection", () => a.page.evaluate(() =>
    window.facecast.debug.shares().peers.find(p => p.on && p.uploads > 0) || null), 30)
  log(`Ana sees Ben's screen (${screen.video}, ${screen.uploads} frames on the projection)`)
  if (screen.at.some((v, i) => Math.abs(v - shared.at[i]) > 0.01)) failures.push(`Ana has Ben's projection at ${screen.at}, not ${shared.at}`)
  if (screen.size !== 2.4 || shared.size !== 2.4) failures.push(`Ben's projection didn't start medium: ${shared.size}, ${screen.size}`)
  // He makes it large (the size buttons show while he shares): it must grow for her too.
  if (await b.page.evaluate(() => document.getElementById('shareSizes').hidden)) failures.push("Ben's size buttons aren't showing")
  await b.page.click('[aria-label="Large screen"]')
  await until("Ana to see Ben's projection grow large", () => a.page.evaluate(() =>
    window.facecast.debug.shares().peers.some(p => p.on && p.size === 3.6)), 10)
  log("Ben made his projection large; Ana's grew too")
  await sleep(600)
  await b.page.screenshot({path: path.join(shots, 'web_share_sizes.png')})
  await a.page.evaluate(([x, z]) => {
    const ax = x - 0.8, az = z - 6
    const yaw = Math.atan2(x - ax, z - az) * 180 / Math.PI
    window.facecast.debug.unity(`teleport ${ax} ${az} ${(yaw + 360) % 360} 2`)
  }, [sx, sz])
  await sleep(2000)
  await a.page.screenshot({path: path.join(shots, 'web_projection.png')})

  // Someone arriving later gets the boards from the people already here.
  const c = await open('Cy')
  await c.page.goto(`${base}?${q}autojoin&name=Cy&cam=0&mic=0#${room}`)
  await until('Cy to join', () => c.page.evaluate(() => window.facecast?.debug.joined()), 120)
  await until('Cy to get the note, where Ben moved it', async () => near(await noteOnBoard(c.page, 'Hello from Ana'), 0.7, 0.4), 60)
  await until('Cy to find the question on the table', () => questionIn(c.page, 'table', "What's for lunch?"), 10)
  log('Cy, arriving later, finds the note where Ben moved it and the question on the table')
  await until("Cy to get Ben's screen", () => c.page.evaluate(() => window.facecast.debug.shares().peers.some(p => p.on && p.uploads > 0)), 30)
  log("…and Ben's projection, with his screen on it")

  // Ben stops sharing: his projection comes down for everyone.
  await b.page.click('#hudShare')
  for (const {name, page} of [a, c])
    await until(`${name} to see Ben's projection come down`, () => page.evaluate(() => !window.facecast.debug.shares().peers.some(p => p.on)), 10)
  if (await b.page.evaluate(() => window.facecast.debug.shares().mine)) failures.push("Ben's page still thinks he's sharing")
  log('Ben stopped sharing; his projection came down for Ana and Cy')
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

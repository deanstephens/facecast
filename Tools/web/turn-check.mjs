// Checks a Facecast site's TURN servers: reads its config.js (turn, and turnUrl's fresh credentials), then for each
// server has two connections in a headless Chrome talk only through it (relay-only, the way people on strict networks
// connect) and says whether a message got across.
//
//   cd Tools/web && npm run turn-check                                        (the local build's config.js)
//   npm run turn-check -- --url https://you.github.io/facecast/               (a deployed site's)
//   npm run turn-check -- --config ../../Assets/WebGLTemplates/Facecast/config.js

import {chromium} from 'playwright-core'
import {fileURLToPath} from 'node:url'
import path from 'node:path'
import fs from 'node:fs'
import vm from 'node:vm'

const here = path.dirname(fileURLToPath(import.meta.url))
const args = process.argv.slice(2)
const arg = name => (args.includes(name) ? args[args.indexOf(name) + 1] : null)
const chrome = process.env.CHROME || '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'

// config.js sets window.FACECAST_CONFIG: run it with a window of its own.
async function readConfig() {
  const url = arg('--url')
  const file = arg('--config') || path.resolve(here, '../../Builds/WebGL/config.js')
  const source = url ? await (await fetch(new URL('config.js', url.endsWith('/') ? url : url + '/'), {cache: 'no-store'})).text()
    : fs.readFileSync(file, 'utf8')
  const sandbox = {window: {}}
  vm.runInNewContext(source, sandbox)
  console.log(`config.js from ${url || path.relative(process.cwd(), file)}`)
  return sandbox.window.FACECAST_CONFIG || {}
}

const isTurn = s => s && [].concat(s.urls || []).some(u => /^turns?:/i.test(u))

const config = await readConfig()
const groups = []
if (Array.isArray(config.turn) && config.turn.some(isTurn)) groups.push({name: 'turn (fixed list)', servers: config.turn.filter(isTurn)})
if (config.turnUrl) {
  try {
    const res = await fetch(config.turnUrl, {cache: 'no-store'})
    if (!res.ok) throw new Error(`${res.status} ${res.statusText}`)
    const data = await res.json()
    const list = (Array.isArray(data) ? data : [].concat(data?.iceServers || [])).filter(isTurn)
    if (!list.length) throw new Error('it returned no TURN servers')
    groups.push({name: 'turnUrl (fetched)', servers: list})
  } catch (e) {
    console.log(`turnUrl: couldn't fetch TURN servers: ${e.message}`)
    process.exitCode = 1
  }
}
if (!groups.length) {
  console.log('No TURN servers configured: people on strict networks will not be able to connect. See config.js.')
  process.exit(1)
}

const browser = await chromium.launch({executablePath: chrome, headless: true})
try {
  const page = await browser.newPage()
  await page.setContent('<!doctype html><title>TURN check</title>')
  for (const group of groups) {
    // Each URL on its own, so a dead port or transport shows up; then the whole group, as the page uses it.
    const tries = group.servers.flatMap(s => [].concat(s.urls).filter(u => /^turns?:/i.test(u)).map(u => ({...s, urls: u})))
    console.log(`${group.name}:`)
    let any = false
    for (const server of tries) {
      const ok = await page.evaluate(relays, [server])
      any ||= ok
      console.log(`  ${ok ? 'relays  ' : 'FAILS   '} ${server.urls}`)
    }
    if (!any) process.exitCode = 1
  }
} finally {
  await browser.close()
}
console.log(process.exitCode ? 'Some TURN servers failed (see above).' : 'TURN works: people on strict networks can connect through it.')

// In the page: two connections allowed to talk only through `iceServers`; true if a message gets across.
async function relays(iceServers) {
  const cfg = {iceServers, iceTransportPolicy: 'relay'}
  const a = new RTCPeerConnection(cfg)
  const b = new RTCPeerConnection(cfg)
  a.onicecandidate = e => e.candidate && b.addIceCandidate(e.candidate).catch(() => {})
  b.onicecandidate = e => e.candidate && a.addIceCandidate(e.candidate).catch(() => {})
  const channel = a.createDataChannel('check')
  const received = new Promise(resolve => { b.ondatachannel = e => { e.channel.onmessage = m => resolve(m.data) } })
  channel.onopen = () => channel.send('through TURN')
  await a.setLocalDescription(await a.createOffer())
  await b.setRemoteDescription(a.localDescription)
  await b.setLocalDescription(await b.createAnswer())
  await a.setRemoteDescription(b.localDescription)
  const result = await Promise.race([received, new Promise(resolve => setTimeout(() => resolve(null), 10000))])
  a.close()
  b.close()
  return result === 'through TURN'
}

// Facecast's page: loads the Unity build, runs the join card, owns your camera and microphone and the WebRTC room,
// plays everyone's voice, and answers the game's calls (window.facecast; see Plugins/WebGL/Facecast.jslib).
//
// Networking is peer to peer: every browser in a room connects to every other one (WebRTC), sending its camera and
// microphone as media streams and two small messages over a data channel: "hello" (name, colour, camera and
// microphone on/off) and "pose" (where its avatar stands and looks, 15 times a second). Browsers find each other
// through Trystero, which swaps connection offers over public Nostr relays (or MQTT brokers), so the page needs no
// server of its own and runs from any static host such as GitHub Pages.

const APP_ID = 'facecast.unity-webrtc.v1'
const COLORS = ['#5AA6F2', '#F2705A', '#F2C14E', '#4CC9A0', '#9B7BF2', '#F27BB4', '#F2963C', '#3CB4C8']
const VIDEO = {width: {ideal: 640}, height: {ideal: 480}, frameRate: {ideal: 24, max: 30}, facingMode: 'user'}
const AUDIO = {echoCancellation: true, noiseSuppression: true, autoGainControl: true}
const EYE_HEIGHT = 1.5
const NAMES = ['Sunny Otter', 'Brave Finch', 'Quiet Fox', 'Lucky Newt', 'Merry Wren', 'Calm Heron', 'Bold Badger', 'Swift Hare']
const ROOM_WORDS = ['mint', 'amber', 'coral', 'maple', 'olive', 'plum', 'sage', 'honey', 'cedar', 'pearl']
const ROOM_PLACES = ['fountain', 'plaza', 'garden', 'square', 'bench', 'lantern', 'hedge', 'meadow']

const $ = id => document.getElementById(id)
const params = new URLSearchParams(location.search)
const settings = {net: 'nostr', relayUrls: null, turn: [], ...(window.FACECAST_CONFIG || {})}
if (params.get('net')) settings.net = params.get('net')

const canvas = $('game')
const feeds = $('feeds')

// ------------------------------------------------------------------ state

const me = {name: '', color: COLORS[0], cam: true, mic: true}
const local = new MediaStream()     // the tracks we send: at most one video and one audio
let localShared = false             // added to the room yet (later tracks then go with addTrack)
const selfVideo = makeVideo(true)   // your camera, for demo bots that wear it
const peers = new Map()             // id -> {id, name, color, cam, mic, hello, pose, stream, video, analyser, source}
let room = null
let actions = null
let selfId = ''
let roomId = ''
let joined = false
let lastPose = null
let unity = null
let unityAsked = false              // the game has called FC_Ready
const unityQueue = []
const uploads = {}
let audioCtx = null
let selfAnalyser = null

// ------------------------------------------------------------------ the game's calls (Facecast.jslib)

window.facecast = {
  unityReady() {
    unityAsked = true
    flushUnity()
  },
  sendPose(x, y, z, yaw, pitch, time) {
    lastPose = [x, y, z, yaw, pitch, time]
    if (actions) actions.pose.send(new Float32Array(lastPose)).catch(() => {})
    for (const peer of peers.values()) setVolume(peer)
  },
  toggleMic() { setMic(!me.mic) },
  toggleCam() { setCam(!me.cam) },
  videoFor(key) {
    if (key === 'self') return me.cam ? selfVideo : null
    const peer = peers.get(key)
    return peer && peer.cam ? peer.video : null
  },
  frameStamp(video) { return video.fcFrames > 0 ? video.fcFrames : video.currentTime },
  countUpload(key) { uploads[key] = (uploads[key] || 0) + 1 },
  audioLevel(key) {
    if (key === 'self') return me.mic ? levelOf(selfAnalyser, me) : 0
    const peer = peers.get(key)
    return peer && peer.mic ? levelOf(peer.analyser, peer) : 0
  },
  // For the console and the end-to-end test (Tools/web/e2e.mjs).
  debug: {
    selfId: () => selfId,
    room: () => roomId,
    joined: () => joined,
    unityLoaded: () => !!unity,
    peers: () => [...peers.values()].map(p => ({
      id: p.id, name: p.name, hello: p.hello, cam: p.cam, mic: p.mic,
      video: p.video ? `${p.video.videoWidth}x${p.video.videoHeight}` : '',
      audio: !!(p.stream && p.stream.getAudioTracks().length),
      uploads: uploads[p.id] || 0,
    })),
    uploads: () => ({...uploads}),
    unity: command => unity && unity.SendMessage('Room', 'OnDebugCommand', command),
  },
}

function toUnity(event) {
  unityQueue.push(JSON.stringify(event))
  flushUnity()
}

function flushUnity() {
  if (!unity || !unityAsked) return
  for (const json of unityQueue.splice(0)) unity.SendMessage('Room', 'OnNetEvent', json)
}

// ------------------------------------------------------------------ loading the game

function loadUnity() {
  const build = window.FACECAST_BUILD
  const config = {
    ...build.config,
    // Sharp enough on high-density screens without drawing four times the pixels.
    devicePixelRatio: Math.min(window.devicePixelRatio || 1, 1.5),
    showBanner: (message, type) => { if (type === 'error') cardError(message); else console.warn(message) },
  }
  const script = document.createElement('script')
  script.src = build.loaderUrl
  script.onload = () => {
    createUnityInstance(canvas, config, progress => {
      $('joinBar').style.width = `${Math.round(progress * 100)}%`
      $('joinLabel').textContent = `Loading the square… ${Math.round(progress * 100)}%`
    }).then(instance => {
      unity = instance
      flushUnity()
      $('joinBar').style.width = '0'
      $('joinLabel').textContent = 'Join'
      $('join').disabled = false
      if (params.has('autojoin')) join()
    }).catch(error => cardError(`The game couldn't start: ${error}`))
  }
  script.onerror = () => cardError("Couldn't load the game files.")
  document.body.appendChild(script)
}

// ------------------------------------------------------------------ the join card

function setupCard() {
  let saved = {}
  try { saved = JSON.parse(localStorage.getItem('facecast') || '{}') } catch {}
  me.name = (params.get('name') || saved.name || '').slice(0, 24)
  me.color = COLORS.includes(saved.color) ? saved.color : COLORS[Math.floor(Math.random() * COLORS.length)]
  if (params.get('color') && COLORS.includes('#' + params.get('color').replace('#', '').toUpperCase()))
    me.color = '#' + params.get('color').replace('#', '').toUpperCase()
  if (params.get('cam') === '0') me.cam = false
  if (params.get('mic') === '0') me.mic = false

  $('name').value = me.name
  $('name').placeholder = NAMES[Math.floor(Math.random() * NAMES.length)]
  $('room').value = cleanRoom(decodeURIComponent(location.hash.slice(1))) || cleanRoom(params.get('room') || '') || randomRoom()

  const swatches = $('swatches')
  for (const color of COLORS) {
    const b = document.createElement('button')
    b.type = 'button'
    b.className = 'swatch'
    b.style.background = color
    b.dataset.color = color
    b.setAttribute('role', 'radio')
    b.setAttribute('aria-label', color)
    b.onclick = () => pickColor(color)
    swatches.appendChild(b)
  }
  pickColor(me.color)

  $('camToggle').onclick = () => setCam(!me.cam)
  $('micToggle').onclick = () => setMic(!me.mic)
  $('previewOn').onclick = () => setCam(true)
  $('join').onclick = join
  for (const id of ['name', 'room']) $(id).addEventListener('keydown', e => { if (e.key === 'Enter' && !$('join').disabled) join() })
  reflectMedia()

  // Show the camera right away if the browser already allows it; otherwise wait for a click, so the
  // permission prompt never appears out of nowhere.
  if (me.cam && navigator.permissions) {
    navigator.permissions.query({name: 'camera'}).then(p => { if (p.state === 'granted' && !joined) setCam(true) }).catch(() => {})
  }
}

function pickColor(color) {
  me.color = color
  document.documentElement.style.setProperty('--me', color)
  for (const b of $('swatches').children) b.setAttribute('aria-checked', String(b.dataset.color === color))
  if (joined) sayHello()
}

function cleanRoom(text) {
  return text.toLowerCase().replace(/[^a-z0-9_-]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 40)
}

function randomRoom() {
  const pick = list => list[Math.floor(Math.random() * list.length)]
  return `${pick(ROOM_WORDS)}-${pick(ROOM_PLACES)}-${Math.floor(10 + Math.random() * 90)}`
}

function cardError(message) {
  $('cardError').textContent = message
  if (joined) toast(message, true)
}

// ------------------------------------------------------------------ camera and microphone

function makeVideo(muted) {
  const v = document.createElement('video')
  v.muted = muted
  v.playsInline = true
  // Count frames as they arrive, so the game copies each one once.
  if ('requestVideoFrameCallback' in v) {
    const tick = () => { v.fcFrames = (v.fcFrames || 0) + 1; v.requestVideoFrameCallback(tick) }
    v.requestVideoFrameCallback(tick)
  }
  feeds.appendChild(v)
  return v
}

function play(video) {
  const p = video.play()
  if (p) p.catch(() => {})
}

async function setCam(on) {
  let track = local.getVideoTracks()[0]
  if (on && !track) {
    try {
      const s = await navigator.mediaDevices.getUserMedia({video: VIDEO})
      track = s.getVideoTracks()[0]
      addLocalTrack(track)
    } catch (e) {
      on = false
      cardError(`Your camera didn't start: ${reason(e)}`)
    }
  }
  if (track) track.enabled = on
  me.cam = on && !!track
  reflectMedia()
  if (joined) sayHello()
}

async function setMic(on) {
  let track = local.getAudioTracks()[0]
  if (on && !track) {
    try {
      const s = await navigator.mediaDevices.getUserMedia({audio: AUDIO})
      track = s.getAudioTracks()[0]
      addLocalTrack(track)
    } catch (e) {
      on = false
      cardError(`Your microphone didn't start: ${reason(e)}`)
    }
  }
  if (track) track.enabled = on
  me.mic = on && !!track
  reflectMedia()
  if (joined) sayHello()
}

// Asks for the camera and microphone together (one permission prompt), falling back to one at a time so a
// missing or refused device doesn't take the other with it.
async function acquireMedia() {
  const video = me.cam && !local.getVideoTracks().length
  const audio = me.mic && !local.getAudioTracks().length
  if (!video && !audio) return
  try {
    const s = await navigator.mediaDevices.getUserMedia({video: video ? VIDEO : false, audio: audio ? AUDIO : false})
    for (const track of s.getTracks()) addLocalTrack(track)
    reflectMedia()
  } catch {
    if (video) await setCam(true)
    if (audio) await setMic(true)
  }
}

function addLocalTrack(track) {
  local.addTrack(track)
  if (track.kind === 'video') {
    for (const v of [$('preview'), $('selfView'), selfVideo]) {
      v.srcObject = local
      play(v)
    }
  } else {
    ensureAudio()
    selfAnalyser = analyse(new MediaStream([track]))
  }
  if (!room) return
  if (localShared) room.addTrack(track, local)
  else {
    room.addStream(local)
    localShared = true
  }
}

function reason(e) {
  if (e && e.name === 'NotAllowedError') return 'permission was refused (check the address bar)'
  if (e && e.name === 'NotFoundError') return 'none was found'
  if (e && e.name === 'NotReadableError') return 'another app is using it'
  return e && e.message ? e.message : String(e)
}

// Card and HUD buttons, previews and "camera off" screens follow me.cam and me.mic.
function reflectMedia() {
  const hasVideo = local.getVideoTracks().length > 0
  for (const id of ['camToggle', 'hudCam']) $(id).setAttribute('aria-pressed', String(me.cam))
  for (const id of ['micToggle', 'hudMic']) $(id).setAttribute('aria-pressed', String(me.mic))
  $('previewOn').hidden = !(me.cam && !hasVideo)
  $('previewOff').hidden = me.cam
  $('preview').hidden = !(me.cam && hasVideo)
  $('selfOff').hidden = me.cam
  $('selfView').hidden = !me.cam
}

// ------------------------------------------------------------------ voice

function ensureAudio() {
  if (!audioCtx) audioCtx = new (window.AudioContext || window.webkitAudioContext)()
  if (audioCtx.state === 'suspended') audioCtx.resume().catch(() => {})
  return audioCtx
}

// An analyser on a stream's sound, for how loud someone is (it plays through their <video>, not through here).
function analyse(stream) {
  if (!stream.getAudioTracks().length) return null
  const ctx = ensureAudio()
  const analyser = ctx.createAnalyser()
  analyser.fftSize = 512
  analyser.fcBuffer = new Float32Array(analyser.fftSize)
  analyser.fcSource = ctx.createMediaStreamSource(stream)
  analyser.fcSource.connect(analyser)
  return analyser
}

function levelOf(analyser, who) {
  if (!analyser) return 0
  const now = performance.now()
  if (who.levelAt === now) return who.level
  analyser.getFloatTimeDomainData(analyser.fcBuffer)
  let sum = 0
  for (const s of analyser.fcBuffer) sum += s * s
  const db = 10 * Math.log10(sum / analyser.fcBuffer.length + 1e-12)
  who.level = Math.min(1, Math.max(0, (db + 52) / 32))
  who.levelAt = now
  return who.level
}

// Voices fade with distance, so people nearby are easy to hear and a crowd across the square isn't.
function setVolume(peer) {
  if (!peer.video || !peer.pose || !lastPose) return
  const d = Math.hypot(peer.pose[0] - lastPose[0], peer.pose[1] - lastPose[1], peer.pose[2] - lastPose[2])
  peer.video.volume = Math.max(0.06, Math.min(1, 2.2 / Math.max(d, 0.1)))
}

// ------------------------------------------------------------------ the room

function hello() {
  return {name: me.name, color: me.color, cam: me.cam, mic: me.mic}
}

function sayHello(target) {
  if (actions) actions.hello.send(hello(), target ? {target} : undefined).catch(() => {})
}

function peer(id) {
  let p = peers.get(id)
  if (!p) {
    p = {id, name: 'Guest', color: COLORS[0], cam: false, mic: false, hello: false, pose: null, stream: null, video: null, analyser: null}
    peers.set(id, p)
  }
  return p
}

async function join() {
  if (joined || $('join').disabled) return
  $('join').disabled = true
  $('cardError').textContent = ''
  me.name = ($('name').value.trim() || $('name').placeholder).slice(0, 24)
  roomId = cleanRoom($('room').value) || randomRoom()
  $('room').value = roomId
  history.replaceState(null, '', `${location.pathname}${location.search}#${roomId}`)
  try { localStorage.setItem('facecast', JSON.stringify({name: me.name, color: me.color})) } catch {}

  ensureAudio()   // inside the click, so the browser lets sound play
  await acquireMedia()

  let trystero
  try {
    trystero = await import(settings.net === 'mqtt' ? './lib/trystero-mqtt.js' : './lib/trystero-nostr.js')
  } catch (e) {
    $('join').disabled = false
    return cardError(`Couldn't load the networking library: ${e.message || e}`)
  }
  const config = {appId: APP_ID}
  if (settings.relayUrls) config.relayConfig = {urls: settings.relayUrls}
  if (settings.turn && settings.turn.length) config.turnConfig = settings.turn
  room = trystero.joinRoom(config, roomId, {
    onJoinError: ({error, peerId}) => {
      console.warn('[facecast] connection failed', peerId, error)
      toast(`Couldn't connect to someone (${error}). Their network may need a TURN server; see config.js.`, true)
    },
  })
  selfId = trystero.selfId
  const helloAction = room.makeAction('hello')
  const poseAction = room.makeAction('pose')
  actions = {hello: helloAction, pose: poseAction}

  room.onPeerJoin = id => {
    peer(id)
    helloAction.send(hello(), {target: id}).catch(() => {})
    if (lastPose) poseAction.send(new Float32Array(lastPose), {target: id}).catch(() => {})
    if (local.getTracks().length) room.addStream(local, {target: id})
    updatePeople()
  }
  room.onPeerLeave = id => {
    const p = peers.get(id)
    if (!p) return
    peers.delete(id)
    if (p.video) { p.video.srcObject = null; p.video.remove() }
    if (p.analyser) { p.analyser.fcSource.disconnect(); p.analyser.disconnect() }
    toUnity({type: 'leave', id})
    if (p.hello) toast(`${p.name} left`)
    updatePeople()
  }
  room.onPeerStream = (stream, id) => attachStream(peer(id), stream)
  room.onPeerTrack = (track, stream, id) => attachStream(peer(id), stream)

  helloAction.onMessage = (data, {peerId}) => {
    const p = peer(peerId)
    const first = !p.hello
    p.name = typeof data?.name === 'string' && data.name.trim() ? data.name.trim().slice(0, 24) : 'Guest'
    p.color = typeof data?.color === 'string' && /^#[0-9a-f]{6}$/i.test(data.color) ? data.color : COLORS[0]
    p.cam = !!data?.cam
    p.mic = !!data?.mic
    p.hello = true
    toUnity({type: 'hello', id: peerId, name: p.name, color: p.color, cam: p.cam, mic: p.mic})
    if (first) {
      toast(`${p.name} joined`)
      if (p.pose) toUnity({type: 'pose', id: peerId, p: p.pose})
    }
    updatePeople()
  }
  poseAction.onMessage = (data, {peerId}) => {
    const buffer = data instanceof ArrayBuffer ? data
      : ArrayBuffer.isView(data) ? data.buffer.slice(data.byteOffset, data.byteOffset + data.byteLength) : null
    if (!buffer || buffer.byteLength < 24) return
    const f = new Float32Array(buffer, 0, 6)
    const p = peer(peerId)
    p.pose = Array.from(f.subarray(0, 6))
    setVolume(p)
    if (p.hello) toUnity({type: 'pose', id: peerId, p: p.pose})
  }

  if (local.getTracks().length) {
    room.addStream(local)
    localShared = true
  }
  joined = true
  toUnity({type: 'self', id: selfId, name: me.name, color: me.color, room: roomId})
  showHud()
}

function attachStream(p, stream) {
  p.stream = stream
  if (!p.video) p.video = makeVideo(false)  // their voice plays through this element
  if (p.video.srcObject !== stream) p.video.srcObject = stream
  play(p.video)
  if (!p.analyser && stream.getAudioTracks().length) p.analyser = analyse(stream)
  setVolume(p)
}

// ------------------------------------------------------------------ the HUD

function showHud() {
  $('card').hidden = true
  $('hud').hidden = false
  $('roomLabel').textContent = roomId
  $('hudMic').onclick = () => setMic(!me.mic)
  $('hudCam').onclick = () => setCam(!me.cam)
  $('leave').onclick = leave
  $('invite').onclick = copyInvite
  updatePeople()
  reflectMedia()
  canvas.focus()
  // Light the microphone button with your own voice.
  const meter = () => {
    $('hudMic').style.setProperty('--level', String(me.mic ? levelOf(selfAnalyser, me) : 0))
    requestAnimationFrame(meter)
  }
  meter()
  if (matchMedia('(pointer: coarse)').matches && !matchMedia('(any-pointer: fine)').matches)
    toast('Facecast needs a keyboard and mouse to walk around; others can still see and hear you.')
}

function updatePeople() {
  const here = [...peers.values()].filter(p => p.hello).length
  $('peopleLabel').textContent = here === 0 ? 'waiting for others' : here === 1 ? '2 here' : `${here + 1} here`
  $('netDot').classList.toggle('live', here > 0)
}

async function copyInvite() {
  const link = `${location.origin}${location.pathname}#${roomId}`
  try {
    await navigator.clipboard.writeText(link)
    toast('Invite link copied. Send it to friends to meet here.')
  } catch {
    toast(link)
  }
}

function leave() {
  if (room) room.leave()
  for (const t of local.getTracks()) t.stop()
  location.reload()
}

function toast(message, bad = false) {
  const t = document.createElement('div')
  t.className = 'toast' + (bad ? ' bad' : '')
  t.textContent = message
  $('toasts').appendChild(t)
  setTimeout(() => t.classList.add('fade'), bad ? 7000 : 3500)
  setTimeout(() => t.remove(), bad ? 7500 : 4000)
}

// ------------------------------------------------------------------ start

// The game listens to the keyboard on the whole page, which would swallow typing in the name and room fields.
// This listener goes in first (capture phase, before the game loads) and keeps keys aimed at a text field away from
// the game's handlers; the field still gets them, since nothing cancels their default action.
for (const type of ['keydown', 'keyup', 'keypress']) {
  window.addEventListener(type, e => {
    const t = e.target
    if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable)) e.stopImmediatePropagation()
  }, true)
}
canvas.addEventListener('pointerdown', () => canvas.focus())
canvas.addEventListener('contextmenu', e => e.preventDefault())
window.addEventListener('pagehide', () => { if (room) room.leave() })
setupCard()
loadUnity()

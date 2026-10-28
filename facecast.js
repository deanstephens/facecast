// Facecast's page: loads the Unity build, runs the join card, owns your camera and microphone and the WebRTC room,
// plays everyone's voice, shows the sticky note you write on, and answers the game's calls (window.facecast; see
// Plugins/WebGL/Facecast.jslib).
//
// Networking is peer to peer: every browser in a room connects to every other one (WebRTC), sending its camera and
// microphone as media streams and small messages over a data channel: "hello" (name, colour, camera and microphone
// on/off), "pose" (where its avatar stands and looks, 15 times a second, with the part of its camera picture its face
// is in: see faceframe.js), "share" (where its shared screen's projection stands; the screen itself is a second
// media stream, marked {screen: true}) and "notes" (sticky notes on the
// noticeboards: the game decides what's on them, the page just carries the messages). Browsers find each other
// through Trystero, which swaps connection offers over public Nostr relays (or MQTT brokers), so the page needs no
// server of its own and runs from any static host such as GitHub Pages.

import {FaceZoom, cleanCrop} from './faceframe.js'

const APP_ID = 'facecast.unity-webrtc.v1'
const COLORS = ['#5AA6F2', '#F2705A', '#F2C14E', '#4CC9A0', '#9B7BF2', '#F27BB4', '#F2963C', '#3CB4C8']
const VIDEO = {width: {ideal: 640}, height: {ideal: 480}, frameRate: {ideal: 24, max: 30}, facingMode: 'user'}
const AUDIO = {echoCancellation: true, noiseSuppression: true, autoGainControl: true}
const EYE_HEIGHT = 1.5
const NAMES = ['Sunny Otter', 'Brave Finch', 'Quiet Fox', 'Lucky Newt', 'Merry Wren', 'Calm Heron', 'Bold Badger', 'Swift Hare']
const ROOM_WORDS = ['mint', 'amber', 'coral', 'maple', 'olive', 'plum', 'sage', 'honey', 'cedar', 'pearl']
const ROOM_PLACES = ['fountain', 'plaza', 'garden', 'square', 'bench', 'lantern', 'hedge', 'meadow']
// The notepads on every noticeboard, in the game's order (Notes.Colors).
const NOTE_COLORS = ['#FFE27A', '#FFA9C6', '#9DD7FF', '#B6EA98']
const NOTE_NAMES = ['Yellow', 'Pink', 'Blue', 'Green']
const NOTE_MAX = 140

const $ = id => document.getElementById(id)
const params = new URLSearchParams(location.search)
const settings = {net: 'nostr', relayUrls: null, turn: [], turnUrl: '', screenSize: 'medium', ...(window.FACECAST_CONFIG || {})}
if (params.get('net')) settings.net = params.get('net')

const canvas = $('game')
const feeds = $('feeds')

// ------------------------------------------------------------------ state

const me = {name: '', color: COLORS[0], cam: true, mic: true, zoom: true, screenSize: 'medium'}
const local = new MediaStream()     // the tracks we send: at most one video and one audio
let localShared = false             // added to the room yet (later tracks then go with addTrack)
const selfVideo = makeVideo(true)   // your camera, for demo bots that wear it and for finding your face
const zoom = new FaceZoom(selfVideo)
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
let writing = false                 // the note (or question card) to write on is open
let asking = false                  // it's a question card
let noteColor = 0
let notesReport = null              // what the game says is on the boards (for the tests)
let notesReceived = 0
let turn = {servers: [], fetched: false, error: ''}  // the TURN servers this page joined with
let turnCheck = null                // the connection check's results (?turncheck, or the button on a connection error)
let lastConnectionError = 0
let share = null                    // your shared screen: {stream, video, at: [x, y, z, yaw] once the game has put it up}
const SHARE_VIDEO = {frameRate: {ideal: 15, max: 30}, width: {max: 1920}, height: {max: 1080}}
// The sizes a shared screen's projection comes in, by the width of its screen in metres (the game's Projections.Widths).
const SCREEN_SIZES = [
  {id: 'small', label: 'S', name: 'Small', width: 1.6},
  {id: 'medium', label: 'M', name: 'Medium', width: 2.4},
  {id: 'large', label: 'L', name: 'Large', width: 3.6},
  {id: 'huge', label: 'XL', name: 'Huge', width: 5},
]
const fieldKeys = new Map()         // text field -> its keydown handler (see the key filter at the bottom)
let speakerId = ''                  // where everyone's voices play (?speaker=…), once found
// A program at your controls (?agent=…): its connection, and how many commands and answers have passed.
const agent = {url: params.get('agent') || '', socket: null, connected: false, commands: 0, answers: 0, retry: 1000}
// On a touch screen: the on-screen controls, as the game will next read them (see "on a touch screen" below).
const touch = {on: false, stick: null, looks: new Map(), move: [0, 0], lookX: 0, lookY: 0, run: false, jump: false,
  use: false, write: false, drop: false}

// ------------------------------------------------------------------ the game's calls (Facecast.jslib)

window.facecast = {
  unityReady() {
    unityAsked = true
    flushUnity()
  },
  sendPose(x, y, z, yaw, pitch, time) {
    lastPose = [x, y, z, yaw, pitch, time]
    if (actions) actions.pose.send(poseMessage()).catch(() => {})
    for (const peer of peers.values()) setVolume(peer)
  },
  toggleMic() { setMic(!me.mic) },
  toggleCam() { setCam(!me.cam) },
  toggleZoom() { setZoom(!me.zoom) },
  toggleShare() { if (share) stopShare(); else startShare() },
  resizeShare(step) { stepScreenSize(step) },
  // The game put up your projection here (x, y, z, yaw): now everyone gets it, and your screen.
  shareAt(x, y, z, yaw) {
    if (!share) return
    share.at = [x, y, z, yaw]
    if (!actions) return
    actions.share.send(shareMessage()).catch(() => {})
    room.addStream(share.stream, {metadata: {screen: true}})
  },
  videoFor(key) {
    if (key === 'screen:self') return share ? share.video : null
    if (key.startsWith('screen:')) {
      const p = peers.get(key.slice(7))
      return p && p.share && p.share.on ? p.screenVideo : null
    }
    if (key === 'self') return me.cam ? selfVideo : null
    const peer = peers.get(key)
    return peer && peer.cam ? peer.video : null
  },
  frameStamp(video) { return video.fcFrames > 0 ? video.fcFrames : video.currentTime },
  // The part of key's picture their face is in, [x, y, w, h] from the top left, or null for the whole picture.
  videoCrop(key) {
    const c = key === 'self' ? ownCrop() : peers.get(key)?.crop
    return c ? [c.x, c.y, c.w, c.h] : null
  },
  countUpload(key) { uploads[key] = (uploads[key] || 0) + 1 },
  audioLevel(key) {
    if (key === 'self') return me.mic ? levelOf(selfAnalyser, me) : 0
    const peer = peers.get(key)
    return peer && peer.mic ? levelOf(peer.analyser, peer) : 0
  },
  sendNotes(json, target) {
    if (!actions) return
    let data
    try { data = JSON.parse(json) } catch { return }
    if (!target) actions.notes.send(data).catch(() => {})
    else if (peers.has(target)) actions.notes.send(data, {target}).catch(() => {})  // demo bots aren't peers
  },
  openNoteEditor(text, color, question) { openNoteEditor(text, color, question) },
  closeNoteEditor() { closeNoteEditor() },
  reportNotes(json) { try { notesReport = JSON.parse(json) } catch {} },
  agentReply(json) { agentAnswer(json) },
  // The on-screen controls since the game last asked (it asks every frame), or null when they aren't showing.
  touchControls() {
    if (!touch.on) return null
    const t = [touch.move[0], touch.move[1], touch.lookX, touch.lookY, +touch.run, +touch.jump, +touch.use, +touch.write, +touch.drop]
    touch.lookX = touch.lookY = 0
    touch.jump = touch.use = touch.write = touch.drop = false
    return t
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
      crop: p.crop,
      pose: p.pose,
    })),
    faceZoom: () => ({enabled: zoom.enabled, state: zoom.state, faces: zoom.faces, crop: zoom.crop}),
    // Your share, and everyone's: where their projection is, the picture's size and how many frames reached it.
    shares: () => ({
      mine: share ? {at: share.at, size: share.size, video: `${share.video.videoWidth}x${share.video.videoHeight}`,
        uploads: uploads['screen:self'] || 0} : null,
      peers: [...peers.values()].filter(p => p.share).map(p => ({
        id: p.id, on: p.share.on, at: p.share.at, size: p.share.size,
        video: p.screenVideo ? `${p.screenVideo.videoWidth}x${p.screenVideo.videoHeight}` : '',
        uploads: uploads[`screen:${p.id}`] || 0,
      })),
    }),
    uploads: () => ({...uploads}),
    pose: () => lastPose,
    turn: () => ({count: turn.servers.length, urls: turn.servers.flatMap(s => [].concat(s.urls)), fetched: turn.fetched,
      error: turn.error, relayOnly: params.has('relay')}),
    turnCheck: () => turnCheck,
    notes: () => notesReport,
    notesReceived: () => notesReceived,
    writing: () => writing,
    asking: () => writing && asking,
    agent: () => ({url: agent.url, connected: agent.connected, commands: agent.commands, answers: agent.answers}),
    speaker: () => speakerId,
    touch: () => ({on: touch.on, stick: !!touch.stick, move: touch.move, run: touch.run}),
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
  me.zoom = params.has('zoom') ? params.get('zoom') !== '0' : saved.zoom !== false
  me.screenSize = [params.get('screen'), saved.screenSize, settings.screenSize].find(id => SCREEN_SIZES.some(s => s.id === id)) || 'medium'
  zoom.setEnabled(me.zoom)

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
  $('zoomToggle').onclick = () => setZoom(!me.zoom)
  $('previewOn').onclick = () => setCam(true)
  $('join').onclick = join
  for (const id of ['name', 'room']) fieldKeys.set($(id), e => { if (e.key === 'Enter' && !$('join').disabled) join() })
  reflectMedia()

  // Show the camera right away if the browser already allows it; otherwise wait for a click, so the
  // permission prompt never appears out of nowhere.
  if (me.cam && navigator.permissions) {
    navigator.permissions.query({name: 'camera'}).then(p => { if (p.state === 'granted' && !joined) setCam(true) }).catch(() => {})
  }
}

function savePrefs() {
  try {
    localStorage.setItem('facecast', JSON.stringify({name: me.name, color: me.color, zoom: me.zoom, screenSize: me.screenSize}))
  } catch {}
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
      const s = await navigator.mediaDevices.getUserMedia({audio: await micAudio()})
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

function setZoom(on) {
  me.zoom = on
  zoom.setEnabled(on)
  savePrefs()
  reflectMedia()
  if (joined) toast(on ? 'Face zoom on: your face fills your screen' : 'Face zoom off: everyone sees your whole picture')
}

// Where your face is in your picture, while your camera is on.
function ownCrop() {
  return me.cam && local.getVideoTracks().length ? zoom.crop : null
}

// Follows your face every frame, and shows your previews zoomed the way everyone else sees you.
function followFace(now) {
  if (me.cam && local.getVideoTracks().length) zoom.tick(now)
  const crop = ownCrop()
  showCrop($('preview'), crop)
  showCrop($('selfView'), crop)
  requestAnimationFrame(followFace)
}

// Fills a mirrored self view with the crop of the picture: the video is stretched over a box that much bigger than
// the screen and shifted so the crop lands on it (its shape matches the picture's, so nothing is distorted).
function showCrop(video, c) {
  const key = c ? `${c.x.toFixed(4)} ${c.y.toFixed(4)} ${c.w.toFixed(4)} ${c.h.toFixed(4)}` : ''
  if (video.fcCrop === key) return
  video.fcCrop = key
  const s = video.style
  if (!c) {
    s.objectFit = s.width = s.height = s.left = s.top = ''
    return
  }
  s.objectFit = 'fill'
  s.width = `${100 / c.w}%`
  s.height = `${100 / c.h}%`
  s.left = `${(-(1 - c.x - c.w) / c.w) * 100}%`  // mirrored: the crop's right edge shows on the left
  s.top = `${(-c.y / c.h) * 100}%`
}

// Asks for the camera and microphone together (one permission prompt), falling back to one at a time so a
// missing or refused device doesn't take the other with it.
async function acquireMedia() {
  const video = me.cam && !local.getVideoTracks().length
  const audio = me.mic && !local.getAudioTracks().length
  if (!video && !audio) return
  try {
    const s = await navigator.mediaDevices.getUserMedia({video: video ? VIDEO : false, audio: audio ? await micAudio() : false})
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
  for (const id of ['zoomToggle', 'hudZoom']) $(id).setAttribute('aria-pressed', String(me.zoom))
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

// Voices fade with distance, so people nearby are easy to hear and a crowd across the square isn't. A shared screen's
// sound (if they shared a tab's sound) comes from its projection.
function setVolume(peer) {
  if (!lastPose) return
  if (peer.video && peer.pose) peer.video.volume = loudness(peer.pose, 2.2)
  if (peer.screenVideo && peer.share && peer.share.on) peer.screenVideo.volume = loudness(peer.share.at, 3)
}

function loudness(at, near) {
  const d = Math.hypot(at[0] - lastPose[0], at[1] - lastPose[1], at[2] - lastPose[2])
  return Math.max(0.06, Math.min(1, near / Math.max(d, 0.1)))
}

// ?micdevice=… picks your microphone and ?speaker=… where everyone's voices play, by (part of) the device's name:
// ?micdevice=BlackHole%2016ch&speaker=BlackHole%202ch routes them through virtual audio cables, so another program
// (a voice assistant, say) can hear the room and speak in it. Otherwise the browser's default devices.
async function findDevice(kind, name) {
  const match = list => list.find(d => d.kind === kind && d.label.toLowerCase().includes(name.toLowerCase())) || null
  let devices = await navigator.mediaDevices.enumerateDevices()
  if (!devices.some(d => d.label)) {
    // Browsers only name devices once the page may use one.
    try { (await navigator.mediaDevices.getUserMedia({audio: true})).getTracks().forEach(t => t.stop()) } catch {}
    devices = await navigator.mediaDevices.enumerateDevices()
  }
  return match(devices)
}

async function micAudio() {
  const name = params.get('micdevice')
  if (!name) return AUDIO
  const d = await findDevice('audioinput', name)
  if (d) return {...AUDIO, deviceId: {exact: d.deviceId}}
  toast(`There's no microphone called "${name}": using the usual one`, true)
  return AUDIO
}

async function pickSpeaker() {
  const name = params.get('speaker')
  if (!name || speakerId) return
  const d = await findDevice('audiooutput', name)
  if (!d) return toast(`There's no speaker called "${name}": voices play on the usual one`, true)
  speakerId = d.deviceId
  for (const p of peers.values()) for (const v of [p.video, p.screenVideo]) if (v) toSpeaker(v)
}

// Sends a voice element's sound to the ?speaker= device. Chrome ignores a device set before the element has its
// stream (it goes on playing on the default one, while reporting the new one), and setting the same device again
// changes nothing, so this comes after each new stream, and goes by way of the default device if the element
// already claims this one.
async function toSpeaker(video) {
  if (!speakerId || !video.setSinkId || !video.srcObject) return
  try {
    if (video.sinkId === speakerId) await video.setSinkId('')
    await video.setSinkId(speakerId)
  } catch (e) {
    console.warn('[facecast] speaker:', e.message)
  }
}

// ------------------------------------------------------------------ sharing your screen

async function startShare() {
  if (!joined || share) return
  if (!navigator.mediaDevices?.getDisplayMedia) return toast("This browser can't share its screen.", true)
  let stream
  for (const audio of [true, false]) {  // a tab's sound if they choose to share it; some browsers refuse to ask
    try {
      stream = await navigator.mediaDevices.getDisplayMedia({video: SHARE_VIDEO, audio})
      break
    } catch (e) {
      if (!e || e.name === 'NotAllowedError' || e.name === 'AbortError') return  // they changed their mind
      if (!audio) return toast(`Couldn't share your screen: ${reason(e)}`, true)
    }
  }
  const track = stream.getVideoTracks()[0]
  if (!track) return
  track.contentHint = 'detail'                       // sharp text over smooth motion
  track.addEventListener('ended', stopShare)          // the browser's own "Stop sharing"
  const video = makeVideo(true)
  video.srcObject = stream
  play(video)
  share = {stream, video, at: null, size: screenWidth()}
  reflectShare()
  toUnity({type: 'sharing', on: true, size: share.size})  // the game puts it up and calls shareAt
  toast('Sharing your screen: it shows on a screen in front of you, where you stood. P or the button stops.')
}

function stopShare() {
  if (!share) return
  const ended = share
  share = null
  for (const t of ended.stream.getTracks()) t.stop()
  if (room && ended.at) {
    room.removeStream(ended.stream)
    if (actions) actions.share.send({on: false}).catch(() => {})
  }
  ended.video.srcObject = null
  ended.video.remove()
  toUnity({type: 'sharing', on: false})
  reflectShare()
}

function shareMessage() {
  return share && share.at ? {on: true, at: share.at, size: share.size} : {on: false}
}

function screenWidth() {
  return (SCREEN_SIZES.find(s => s.id === me.screenSize) || SCREEN_SIZES[1]).width
}

// Your projection's size: remembered for next time, and while you share it grows or shrinks for everyone.
function setScreenSize(id) {
  if (!SCREEN_SIZES.some(s => s.id === id)) return
  me.screenSize = id
  savePrefs()
  reflectShare()
  if (!share) return
  share.size = screenWidth()
  toUnity({type: 'shareSize', size: share.size})
  if (share.at && actions) actions.share.send(shareMessage()).catch(() => {})
}

function stepScreenSize(step) {
  const i = SCREEN_SIZES.findIndex(s => s.id === me.screenSize)
  const next = SCREEN_SIZES[Math.max(0, Math.min(SCREEN_SIZES.length - 1, (i < 0 ? 1 : i) + Math.sign(step)))]
  setScreenSize(next.id)
  if (!share) toast(`Next time you share your screen, it goes up ${next.name.toLowerCase()} (${next.width} m wide)`)
}

function setupShareSizes() {
  for (const size of SCREEN_SIZES) {
    const b = document.createElement('button')
    b.type = 'button'
    b.textContent = size.label
    b.dataset.size = size.id
    b.title = `${size.name}: ${size.width} m wide ([ and ] in the square)`
    b.setAttribute('role', 'radio')
    b.setAttribute('aria-label', `${size.name} screen`)
    b.onclick = () => setScreenSize(size.id)
    $('shareSizes').appendChild(b)
  }
}

function reflectShare() {
  $('hudShare').setAttribute('aria-pressed', String(!!share))
  $('hudShare').title = share ? 'Stop sharing your screen (P)' : 'Share your screen (P)'
  $('shareSizes').hidden = !share
  for (const b of $('shareSizes').children) b.setAttribute('aria-checked', String(b.dataset.size === me.screenSize))
}

// A peer's shared screen: its picture (and sound, if they shared a tab's sound) arrives as a second stream.
function attachScreen(p, stream) {
  p.screenStream = stream
  if (!p.screenVideo) p.screenVideo = makeVideo(false)
  if (p.screenVideo.srcObject !== stream) {
    p.screenVideo.srcObject = stream
    toSpeaker(p.screenVideo)
  }
  play(p.screenVideo)
  setVolume(p)
}

function dropScreen(p) {
  p.screenStream = null
  if (p.screenVideo) {
    p.screenVideo.srcObject = null
    p.screenVideo.remove()
    p.screenVideo = null
  }
}

// ------------------------------------------------------------------ the room

// Your pose for the room: six floats, then the crop of your picture your face is in, while your camera is on.
function poseMessage() {
  const c = ownCrop()
  return new Float32Array(c ? [...lastPose, c.x, c.y, c.w, c.h] : lastPose)
}

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
  savePrefs()

  ensureAudio()   // inside the click, so the browser lets sound play
  await acquireMedia()
  await pickSpeaker()

  let trystero
  try {
    trystero = await import(settings.net === 'mqtt' ? './lib/trystero-mqtt.js' : './lib/trystero-nostr.js')
  } catch (e) {
    $('join').disabled = false
    return cardError(`Couldn't load the networking library: ${e.message || e}`)
  }
  const config = {appId: APP_ID}
  if (settings.relayUrls) config.relayConfig = {urls: settings.relayUrls}
  turn = await turnServers()
  if (turn.servers.length) config.turnConfig = turn.servers
  // ?relay sends everything through the TURN servers, the way people on strict networks connect: a test that they work.
  if (params.has('relay')) config.rtcConfig = {iceTransportPolicy: 'relay'}
  room = trystero.joinRoom(config, roomId, {
    onJoinError: ({error, peerId}) => {
      console.warn('[facecast] connection failed', peerId, error)
      if (/password/.test(error)) return  // a stray offer from another room's browser, not someone here
      // One message for a burst of failures (a reload can leave a few stale offers behind).
      if (performance.now() - lastConnectionError < 20000) return
      lastConnectionError = performance.now()
      toast(turn.servers.length
        ? "Couldn't connect to someone. Their network or yours may be blocking the connection, even through this site's TURN server."
        : "Couldn't connect to someone: one of your networks blocks direct connections, and this site has no TURN server to relay through.",
        true, {label: 'Check my connection', onclick: runTurnCheck})
    },
  })
  selfId = trystero.selfId
  const helloAction = room.makeAction('hello')
  const poseAction = room.makeAction('pose')
  const notesAction = room.makeAction('notes')
  const shareAction = room.makeAction('share')
  actions = {hello: helloAction, pose: poseAction, notes: notesAction, share: shareAction}

  room.onPeerJoin = id => {
    peer(id)
    helloAction.send(hello(), {target: id}).catch(() => {})
    if (lastPose) poseAction.send(poseMessage(), {target: id}).catch(() => {})
    if (local.getTracks().length) room.addStream(local, {target: id})
    if (share && share.at) {
      shareAction.send(shareMessage(), {target: id}).catch(() => {})
      room.addStream(share.stream, {target: id, metadata: {screen: true}})
    }
    updatePeople()
  }
  room.onPeerLeave = id => {
    const p = peers.get(id)
    if (!p) return
    peers.delete(id)
    if (p.video) { p.video.srcObject = null; p.video.remove() }
    if (p.analyser) { p.analyser.fcSource.disconnect(); p.analyser.disconnect() }
    dropScreen(p)
    toUnity({type: 'leave', id})
    if (p.hello) toast(`${p.name} left`)
    updatePeople()
  }
  // A peer's camera and microphone, or (marked {screen: true}) their shared screen.
  room.onPeerStream = (stream, id, meta) => meta?.screen ? attachScreen(peer(id), stream) : attachStream(peer(id), stream)
  room.onPeerTrack = (track, stream, id, meta) => meta?.screen ? attachScreen(peer(id), stream) : attachStream(peer(id), stream)

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
    p.crop = buffer.byteLength >= 40 ? cleanCrop(new Float32Array(buffer, 24, 4)) : null
    setVolume(p)
    if (p.hello) toUnity({type: 'pose', id: peerId, p: p.pose})
  }
  shareAction.onMessage = (data, {peerId}) => {
    const p = peer(peerId)
    const at = Array.isArray(data?.at) && data.at.length === 4 && data.at.every(Number.isFinite) ? data.at.map(Number) : null
    const on = data?.on === true && !!at
    const size = Number.isFinite(data?.size) ? Math.max(1, Math.min(6, data.size)) : 2.4  // older pages: medium
    const first = on && !p.share?.on
    p.share = {on, at, size}
    if (!on) dropScreen(p)
    else {
      setVolume(p)
      if (first && p.hello) toast(`${p.name} is sharing their screen`)
    }
    toUnity({type: 'share', id: peerId, on, p: at || [0, 0, 0, 0], size})
  }
  notesAction.onMessage = (data, {peerId}) => {
    const notes = Array.isArray(data?.notes) ? data.notes.slice(0, 1000).map(cleanNote).filter(Boolean) : []
    if (!notes.length) return
    notesReceived += notes.length
    toUnity({type: 'notes', id: peerId, notes})
  }

  if (local.getTracks().length) {
    room.addStream(local)
    localShared = true
  }
  joined = true
  toUnity({type: 'self', id: selfId, name: me.name, color: me.color, room: roomId})
  agentHello()
  showHud()
}

// TURN servers relay video for people whose networks block direct connections: the fixed list in config.js (turn),
// plus any a provider hands out at turnUrl (fresh credentials each time; Metered's credentials URL, for example). A
// provider that's down mustn't stop anyone joining, so a failed fetch only costs the relay.
async function turnServers() {
  const valid = s => s && typeof s === 'object' && (typeof s.urls === 'string' || Array.isArray(s.urls))
  const servers = (Array.isArray(settings.turn) ? settings.turn : []).filter(valid)
  const result = {servers, fetched: false, error: ''}
  if (!settings.turnUrl) return result
  try {
    const res = await fetch(settings.turnUrl, {cache: 'no-store', signal: AbortSignal.timeout(5000)})
    if (!res.ok) throw new Error(`${res.status} ${res.statusText}`)
    const data = await res.json()
    // An array of ICE servers (Metered), or {iceServers: [...] or {...}} (Cloudflare, others).
    const list = Array.isArray(data) ? data : [].concat(data?.iceServers || [])
    servers.push(...list.filter(valid).filter(s => [].concat(s.urls).some(u => /^turns?:/i.test(u))))
    result.fetched = true
  } catch (e) {
    result.error = String(e?.message || e)
    console.warn("[facecast] couldn't fetch TURN servers from turnUrl:", result.error)
  }
  return result
}

// ------------------------------------------------------------------ the connection check

// Can this computer's network reach the site's TURN server? For each route (UDP, TCP, TLS on their ports), two
// connections in this page may talk only through it; a message getting across means it relays. Open the page with
// ?turncheck, or press "Check my connection" on a connection error. Tells apart a route that got no answer (the network
// blocks it) from one whose login was refused (wrong credentials).
async function runTurnCheck() {
  $('turnCheck').hidden = false
  if (document.pointerLockElement) document.exitPointerLock()
  if (turnCheck && !turnCheck.done) return
  const list = $('turnResults')
  list.textContent = ''
  $('turnVerdict').textContent = 'Checking…'
  turnCheck = {done: false, routes: [], verdict: ''}
  const {servers, error} = await turnServers()
  const routes = servers.flatMap(s => [].concat(s.urls).filter(u => /^turns?:/i.test(u)).map(u => ({...s, urls: u})))
  for (const route of routes) {
    const li = document.createElement('li')
    li.textContent = `${describeRoute(route.urls)}: testing…`
    list.appendChild(li)
    const result = await relayTest(route)
    const how = result.ok ? 'relays ✓' : result.errorCode === 401 ? 'login refused ✗' : 'no answer ✗'
    li.textContent = `${describeRoute(route.urls)}: ${how}`
    li.className = result.ok ? 'good' : 'bad'
    turnCheck.routes.push({url: route.urls, ...result})
  }
  const ok = turnCheck.routes.some(r => r.ok)
  const refused = turnCheck.routes.some(r => r.errorCode === 401)
  turnCheck.verdict = !routes.length
    ? (error ? `This site's TURN servers couldn't be fetched (${error}).` : "This site has no TURN server set up, so people on strict networks can't connect.")
    : ok ? "Your network can reach this site's TURN server: if a direct connection to someone fails, you'll be relayed through it."
    : refused ? "The TURN server refused this site's login: its credentials are wrong or out of date. Whoever runs the site needs to update them."
    : "Your network can't reach this site's TURN server, so people who can't connect to you directly won't see you. Try another network (a phone hotspot, say), or switch off a VPN or firewall."
  turnCheck.done = true
  $('turnVerdict').textContent = turnCheck.verdict
}

// "turn:host:3478", "turn:host:80?transport=tcp", "turns:host:443?transport=tcp" → how a person would say it.
function describeRoute(url) {
  const port = (/:(\d+)(\?|$)/.exec(url) || [])[1] || '3478'
  if (/^turns:/i.test(url)) return `Secure (TLS) on port ${port}`
  return /transport=tcp/i.test(url) ? `TCP on port ${port}` : `UDP on port ${port}`
}

async function relayTest(server, seconds = 8) {
  const config = {iceServers: [server], iceTransportPolicy: 'relay'}
  const a = new RTCPeerConnection(config)
  const b = new RTCPeerConnection(config)
  let errorCode = 0
  a.onicecandidateerror = e => { errorCode = errorCode || e.errorCode }
  a.onicecandidate = e => e.candidate && b.addIceCandidate(e.candidate).catch(() => {})
  b.onicecandidate = e => e.candidate && a.addIceCandidate(e.candidate).catch(() => {})
  const channel = a.createDataChannel('check')
  const received = new Promise(resolve => { b.ondatachannel = e => { e.channel.onmessage = m => resolve(m.data) } })
  channel.onopen = () => channel.send('through TURN')
  try {
    await a.setLocalDescription(await a.createOffer())
    await b.setRemoteDescription(a.localDescription)
    await b.setLocalDescription(await b.createAnswer())
    await a.setRemoteDescription(b.localDescription)
    const got = await Promise.race([received, new Promise(resolve => setTimeout(() => resolve(null), seconds * 1000))])
    return {ok: got === 'through TURN', errorCode}
  } finally {
    a.close()
    b.close()
  }
}

function attachStream(p, stream) {
  p.stream = stream
  if (!p.video) p.video = makeVideo(false)  // their voice plays through this element
  if (p.video.srcObject !== stream) {
    p.video.srcObject = stream
    toSpeaker(p.video)
  }
  play(p.video)
  if (!p.analyser && stream.getAudioTracks().length) p.analyser = analyse(stream)
  setVolume(p)
}

// A note from another browser, with the types the game expects (the game checks the values).
function cleanNote(n) {
  if (!n || typeof n !== 'object' || typeof n.id !== 'string') return null
  const num = (v, d = 0) => typeof v === 'number' && isFinite(v) ? v : d
  const str = (v, max) => typeof v === 'string' ? v.slice(0, max) : ''
  return {
    id: n.id.slice(0, 64), board: num(n.board, -1) | 0, question: n.question === true, x: num(n.x, 0.5),
    y: num(n.y, 0.5), tilt: num(n.tilt), color: num(n.color) | 0, text: str(n.text, 400), by: str(n.by, 48),
    holder: str(n.holder, 64), gone: n.gone === true, v: num(n.v) | 0, w: str(n.w, 64),
  }
}

// ------------------------------------------------------------------ the note you write on

function setupNoteEditor() {
  NOTE_COLORS.forEach((color, i) => {
    const b = document.createElement('button')
    b.type = 'button'
    b.style.background = color
    b.setAttribute('role', 'radio')
    b.setAttribute('aria-label', NOTE_NAMES[i])
    b.onclick = () => pickNoteColor(i)
    $('noteColors').appendChild(b)
  })
  $('noteText').maxLength = NOTE_MAX
  $('noteText').addEventListener('input', countNote)
  fieldKeys.set($('noteText'), e => {
    if (e.key === 'Escape') {
      e.preventDefault()
      finishNote(false)
    } else if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
      e.preventDefault()
      finishNote(true)
    }
  })
  $('noteCancel').onclick = () => finishNote(false)
  $('noteDone').onclick = () => finishNote(true)
}

const MAC = /Mac|iPhone|iPad/.test(navigator.platform)

function pickNoteColor(i) {
  noteColor = Math.max(0, Math.min(NOTE_COLORS.length - 1, i | 0))
  $('notePaper').style.setProperty('--note', NOTE_COLORS[noteColor])
  for (const [k, b] of [...$('noteColors').children].entries()) b.setAttribute('aria-checked', String(k === noteColor))
}

function countNote() {
  $('noteCount').textContent = `${$('noteText').value.length} / ${NOTE_MAX}`
}

// A sticky note of `color` to write on, or a question card (cream, ruled, no colour to choose: questions are anonymous).
function openNoteEditor(text, color, question = false) {
  writing = true
  asking = !!question
  if (document.pointerLockElement) document.exitPointerLock()
  $('noteText').value = text || ''
  $('noteEditor').classList.toggle('asking', asking)
  $('noteColors').hidden = asking
  $('noteText').placeholder = asking ? 'Ask a question…' : 'Write something…'
  $('noteText').setAttribute('aria-label', asking ? 'Your question' : 'What the note says')
  $('noteTip').textContent = touch.on
    ? (asking ? 'Done, then tap the question box to post it' : 'Done, then tap a board to stick it')
    : `${MAC ? '⌘ Enter' : 'Ctrl+Enter'}: done · Esc: cancel · ` + (asking ? 'then post it in the question box' : 'then click a board to stick it')
  if (asking) $('notePaper').style.removeProperty('--note')
  else pickNoteColor(color)
  countNote()
  $('noteEditor').hidden = false
  setTimeout(() => $('noteText').focus(), 0)
}

function closeNoteEditor() {
  writing = false
  $('noteEditor').hidden = true
}

// Done or Cancel: the game hears which, and what's written.
function finishNote(ok) {
  if (!writing) return
  closeNoteEditor()
  toUnity(ok ? {type: 'write', ok: true, text: $('noteText').value.slice(0, NOTE_MAX), tint: noteColor} : {type: 'write', ok: false})
  canvas.focus()
}

// ------------------------------------------------------------------ the HUD

function showHud() {
  $('card').hidden = true
  $('hud').hidden = false
  $('roomLabel').textContent = roomId
  $('hudMic').onclick = () => setMic(!me.mic)
  $('hudCam').onclick = () => setCam(!me.cam)
  $('hudZoom').onclick = () => setZoom(!me.zoom)
  $('hudShare').onclick = () => { if (share) stopShare(); else startShare() }
  $('hudShare').hidden = !navigator.mediaDevices?.getDisplayMedia
  reflectShare()
  $('leave').onclick = leave
  $('invite').onclick = copyInvite
  updatePeople()
  reflectMedia()
  canvas.focus()
  // Light the microphone button with your own voice; on a touch screen, show the write and throw-away buttons while
  // you hold something.
  const meter = () => {
    $('hudMic').style.setProperty('--level', String(me.mic ? levelOf(selfAnalyser, me) : 0))
    const holding = !!notesReport?.held
    if ($('touchWrite').hidden === holding) $('touchWrite').hidden = $('touchDrop').hidden = !holding
    requestAnimationFrame(meter)
  }
  meter()
  if (touch.on) toast('Drag on the left to walk (to the edge to run), anywhere else to look around. Tap to use what’s in the middle.')
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
  if (share) for (const t of share.stream.getTracks()) t.stop()
  location.reload()
}

function toast(message, bad = false, action = null) {
  const t = document.createElement('div')
  t.className = 'toast' + (bad ? ' bad' : '')
  t.textContent = message
  if (action) {
    const b = document.createElement('button')
    b.type = 'button'
    b.textContent = action.label
    b.onclick = () => { t.remove(); action.onclick() }
    t.appendChild(b)
  }
  $('toasts').appendChild(t)
  const stay = action ? 15000 : bad ? 7000 : 3500
  setTimeout(() => t.classList.add('fade'), stay)
  setTimeout(() => t.remove(), stay + 500)
}

// ------------------------------------------------------------------ on a touch screen

// A phone or tablet gets on-screen controls over the game: a finger on the left third walks (the stick jumps under
// it, and pushed past its ring you run), a finger anywhere else looks around (the view follows it, like a mouse),
// and a tap that doesn't drag uses what's under the crosshair, like a click. Buttons jump and, while you hold a
// note, write on it or throw it away. The game reads them every frame (touchControls(); TouchControls.cs). They come
// on by themselves on a touch-only device, or the first time a finger touches the game; a mouse turns them off.
// ?touch forces them on (for trying them with a mouse).
const STICK_REACH = 52      // px from the stick's middle to full speed
const TAP_MOVE = 12         // px a finger may wander and still tap
const TAP_TIME = 350        // ms

function setTouch(on) {
  touch.on = on
  document.body.classList.toggle('touching', on)
  $('touch').hidden = !on
  $('touchButtons').hidden = !on
  if (!on) releaseStick()
}

function releaseStick() {
  touch.stick = null
  touch.move = [0, 0]
  touch.run = false
  const stick = $('stick')
  stick.classList.add('idle')
  stick.classList.remove('running')
  stick.style.removeProperty('left')
  stick.style.removeProperty('top')
  stick.firstElementChild.style.transform = ''
}

function setupTouch() {
  const layer = $('touch')
  const stick = $('stick')
  layer.addEventListener('pointerdown', e => {
    if (e.pointerType === 'mouse') return setTouch(params.has('touch'))
    e.preventDefault()
    layer.setPointerCapture(e.pointerId)
    if (!touch.stick && e.clientX < innerWidth * 0.36) {
      touch.stick = {id: e.pointerId, x: e.clientX, y: e.clientY}
      stick.style.left = `${e.clientX}px`
      stick.style.top = `${e.clientY}px`
      stick.classList.remove('idle')
    } else touch.looks.set(e.pointerId, {x: e.clientX, y: e.clientY, at: performance.now(), wandered: 0})
  })
  layer.addEventListener('pointermove', e => {
    if (touch.stick?.id === e.pointerId) {
      let dx = e.clientX - touch.stick.x
      let dy = e.clientY - touch.stick.y
      const d = Math.hypot(dx, dy)
      touch.run = d > STICK_REACH * 1.35
      if (d > STICK_REACH) { dx *= STICK_REACH / d; dy *= STICK_REACH / d }
      touch.move = [dx / STICK_REACH, -dy / STICK_REACH]
      stick.firstElementChild.style.transform = `translate(${dx}px, ${dy}px)`
      stick.classList.toggle('running', touch.run)
      return
    }
    const look = touch.looks.get(e.pointerId)
    if (!look) return
    const dx = e.clientX - look.x, dy = e.clientY - look.y
    touch.lookX += dx
    touch.lookY -= dy  // the game's y is up
    look.wandered += Math.hypot(dx, dy)
    look.x = e.clientX
    look.y = e.clientY
  })
  const lift = e => {
    if (touch.stick?.id === e.pointerId) return releaseStick()
    const look = touch.looks.get(e.pointerId)
    if (!look) return
    touch.looks.delete(e.pointerId)
    if (e.type === 'pointerup' && look.wandered < TAP_MOVE && performance.now() - look.at < TAP_TIME) touch.use = true
  }
  layer.addEventListener('pointerup', lift)
  layer.addEventListener('pointercancel', lift)
  const button = (id, flag) => $(id).addEventListener('pointerdown', e => {
    e.preventDefault()
    touch[flag] = true
  })
  button('touchJump', 'jump')
  button('touchWrite', 'write')
  button('touchDrop', 'drop')
  // A finger on the game turns the controls on (a laptop with a touch screen); they start on for touch-only devices.
  canvas.addEventListener('pointerdown', e => { if (e.pointerType === 'touch') setTouch(true) })
  setTouch(params.has('touch') || (matchMedia('(pointer: coarse)').matches && !matchMedia('(any-pointer: fine)').matches))
}

// ------------------------------------------------------------------ an agent at your controls

// ?agent=ws://localhost:8765/game connects the page to a program that drives you: an AI that walks you round the
// square and puts up notes when asked, say (voice-wrapper, beside this project, is one). It sends commands as JSON,
// {id, op, …}; the game carries them out (Autopilot.cs) and the answers go back the same way, {id, ok, error, result}.
// The page only carries them (the microphone aside, which is its own), and keeps trying to connect while the
// program isn't running, so either can start first.
function connectAgent() {
  if (!agent.url) return
  let socket
  try {
    socket = new WebSocket(agent.url)
  } catch (e) {
    return console.warn('[facecast] agent:', e.message)
  }
  agent.socket = socket
  socket.onopen = () => {
    agent.connected = true
    agent.retry = 1000
    agentHello()
    console.log('[facecast] agent connected:', agent.url)
    if (joined) toast('Your agent is connected')
  }
  socket.onmessage = e => {
    let command
    try { command = JSON.parse(e.data) } catch { return }
    if (!command || typeof command.op !== 'string') return
    agent.commands++
    agentCommand(command)
  }
  socket.onclose = () => {
    if (agent.connected && joined) toast('Your agent disconnected', true)
    agent.connected = false
    agent.socket = null
    setTimeout(connectAgent, agent.retry)
    agent.retry = Math.min(agent.retry * 2, 10000)
  }
}

// Who and where you are, when the agent connects and when you join.
function agentHello() {
  if (agent.connected) agent.socket.send(JSON.stringify({type: 'hello', name: me.name, room: roomId, joined}))
}

function agentAnswer(answer) {
  if (!agent.connected) return
  agent.answers++
  agent.socket.send(typeof answer === 'string' ? answer : JSON.stringify(answer))
}

async function agentCommand(c) {
  const id = String(c.id ?? '')
  if (c.op === 'mic') {
    await setMic(c.on !== false)
    return agentAnswer({id, ok: true, result: {mic: me.mic}})
  }
  if (!unity || !unityAsked) return agentAnswer({id, ok: false, error: 'The game is still loading'})
  unity.SendMessage('Room', 'OnAgentCommand', JSON.stringify({...c, id}))
}

// ------------------------------------------------------------------ start

// The game listens to the keyboard on the whole page, which would swallow typing in the name, room and note fields.
// This listener goes in first (capture phase, before the game loads) and keeps keys aimed at a text field away from
// the game's handlers; the field still gets them, since nothing cancels their default action. Stopping the event here
// also keeps it from the field's own listeners, so a field's key handling (Enter to join, Esc on a note) is called
// from here instead.
for (const type of ['keydown', 'keyup', 'keypress']) {
  window.addEventListener(type, e => {
    const t = e.target
    if (!t || !(t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable)) return
    if (type === 'keydown') fieldKeys.get(t)?.(e)
    e.stopImmediatePropagation()
  }, true)
}
canvas.addEventListener('pointerdown', () => canvas.focus())
canvas.addEventListener('contextmenu', e => e.preventDefault())
window.addEventListener('pagehide', () => { if (room) room.leave() })
setupCard()
setupNoteEditor()
setupShareSizes()
setupTouch()
$('turnClose').onclick = () => { $('turnCheck').hidden = true }
if (params.has('turncheck')) runTurnCheck()
requestAnimationFrame(followFace)
loadUnity()
connectAgent()

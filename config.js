// The live site's settings, with its TURN credentials. Tools/deploy_pages.sh puts this file in the site in place of
// the template's config.js (Assets/WebGLTemplates/Facecast/config.js). It's kept out of git (.gitignore), so the
// credentials reach only the site, never the repository's history; edit it here, then deploy again.
window.FACECAST_CONFIG = {
  // How browsers find each other: 'nostr' (public Nostr relays, the default) or 'mqtt' (public MQTT brokers).
  // A page address with ?net=mqtt overrides this.
  net: 'nostr',
  // Your own relays or brokers instead of the public defaults (secure WebSocket URLs), or null.
  relayUrls: null,
  // TURN servers relay video for people whose networks block direct connections (some offices, schools and mobile
  // carriers). Without one, those people can't see each other: the page says "Couldn't connect to someone".
  // Either paste a fixed list of servers here (a metered.ca credential's "ICE Servers Array", Twilio, your own coturn):
  //   turn: [{urls: 'turn:turn.example.com:3478', username: 'user', credential: 'secret'}],
  // This site's: a metered.ca credential (its ICE Servers Array), plus UDP on port 3478, which Metered also serves and
  // which gets through networks that block UDP on ports 80 and 443. TCP 80 and TLS 443 get through the strictest.
  turn: [
    {urls: 'stun:stun.relay.metered.ca:80'},
    {urls: 'turn:global.relay.metered.ca:3478', username: '658901e0a8050adc33357ea7', credential: 'UT7oQTMuavN2IBwI'},
    {urls: 'turn:global.relay.metered.ca:80', username: '658901e0a8050adc33357ea7', credential: 'UT7oQTMuavN2IBwI'},
    {urls: 'turn:global.relay.metered.ca:80?transport=tcp', username: '658901e0a8050adc33357ea7', credential: 'UT7oQTMuavN2IBwI'},
    {urls: 'turn:global.relay.metered.ca:443', username: '658901e0a8050adc33357ea7', credential: 'UT7oQTMuavN2IBwI'},
    {urls: 'turns:global.relay.metered.ca:443?transport=tcp', username: '658901e0a8050adc33357ea7', credential: 'UT7oQTMuavN2IBwI'},
  ],
  // ...or a URL that hands out fresh ones each time someone joins: the Cloudflare Worker in Tools/turn-worker, or a
  // metered.ca credential's:
  //   turnUrl: 'https://facecast-turn.yourname.workers.dev/',
  //   turnUrl: 'https://yourapp.metered.live/api/v1/turn/credentials?apiKey=YOUR_API_KEY',
  // Both can be set. Anything here is public (anyone can read config.js), so use a TURN account meant for this.
  // Check them with: cd Tools/web && npm run turn-check -- --url https://you.github.io/facecast/
  turnUrl: '',
  // How big a shared screen's projection starts, until someone picks another size (the page remembers their choice):
  // 'small' (1.6 m wide), 'medium' (2.4 m), 'large' (3.6 m) or 'huge' (5 m). ?screen=large in the address overrides it.
  screenSize: 'medium',
}

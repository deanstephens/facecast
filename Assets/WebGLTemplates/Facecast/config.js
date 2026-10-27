// Settings for this copy of Facecast. Edit them on the server after deploying; no rebuild needed.
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
  turn: [],
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

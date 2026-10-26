// Settings for this copy of Facecast. Edit them on the server after deploying; no rebuild needed.
window.FACECAST_CONFIG = {
  // How browsers find each other: 'nostr' (public Nostr relays, the default) or 'mqtt' (public MQTT brokers).
  // A page address with ?net=mqtt overrides this.
  net: 'nostr',
  // Your own relays or brokers instead of the public defaults (secure WebSocket URLs), or null.
  relayUrls: null,
  // TURN servers relay video for people whose networks block direct connections (some offices, mobile carriers).
  // Without one, those people can't see each other. Example (Cloudflare, Twilio, metered.ca or your own coturn):
  //   turn: [{urls: 'turn:turn.example.com:3478', username: 'user', credential: 'secret'}],
  turn: [],
}

// WP2.2 (F16/F1) — global "session expired" signal. No existing global store in the app
// (checked lib/ — no zustand, no event bus) so this is a plain native EventTarget: zero new
// dependency, and the browser-native pub/sub primitive is all a single boolean-ish signal needs.
export const sessionEvents = new EventTarget();
export const SESSION_EXPIRED_EVENT = 'session-expired';

export function emitSessionExpired(): void {
  sessionEvents.dispatchEvent(new Event(SESSION_EXPIRED_EVENT));
}

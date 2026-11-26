/*
 * Web push service worker (Firebase Cloud Messaging).
 * The Firebase web-app config arrives in this script's own query string at registration time
 * (navigator.serviceWorker.register('/firebase-messaging-sw.js?apiKey=...')), so the committed
 * file stays config-free and web push stays off unless the server serves a complete Push:Web config.
 * These are client-side Firebase values, public by design (they ship in every web app using Firebase).
 */
importScripts('https://www.gstatic.com/firebasejs/10.14.1/firebase-app-compat.js');
importScripts('https://www.gstatic.com/firebasejs/10.14.1/firebase-messaging-compat.js');

(function () {
  var q = new URLSearchParams(self.location.search);
  var cfg = {
    apiKey: q.get('apiKey'),
    authDomain: q.get('authDomain') || undefined,
    projectId: q.get('projectId'),
    messagingSenderId: q.get('messagingSenderId'),
    appId: q.get('appId'),
  };
  if (!cfg.apiKey || !cfg.projectId || !cfg.messagingSenderId || !cfg.appId) return; // registered without config: no-op
  firebase.initializeApp(cfg);
  var messaging = firebase.messaging();
  messaging.onBackgroundMessage(function (payload) {
    var n = (payload && payload.notification) || {};
    self.registration.showNotification(n.title || '', { body: n.body || '', data: (payload && payload.data) || {} });
  });
  self.addEventListener('notificationclick', function (event) {
    event.notification.close();
    event.waitUntil(
      clients.matchAll({ type: 'window', includeUncontrolled: true }).then(function (list) {
        var existing = list.find(function (c) { return c.url.indexOf('/app/') !== -1; });
        if (existing) { existing.focus(); existing.navigate('/app/notifications'); return undefined; }
        return clients.openWindow('/app/notifications');
      })
    );
  });
})();

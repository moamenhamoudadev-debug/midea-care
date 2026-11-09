// MediCare Enterprise Service Worker
const CACHE_NAME = 'medicare-cache-v1';
const STATIC_ASSETS = [
  '/',
  '/manifest.json',
  '/css/site.css',
  '/js/site.js',
  '/lib/bootstrap/dist/css/bootstrap.min.css',
  '/lib/bootstrap/dist/js/bootstrap.bundle.min.js',
  '/lib/jquery/dist/jquery.min.js',
  '/images/pwa-icon-192.png',
  '/images/pwa-icon-512.png',
  '/images/hero-clinic.svg',
  '/offline.html'
];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE_NAME).then((cache) => {
      return cache.addAll(STATIC_ASSETS);
    })
  );
  self.skipWaiting();
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys().then((keys) => {
      return Promise.all(
        keys.map((key) => {
          if (key !== CACHE_NAME) {
            return caches.delete(key);
          }
        })
      );
    })
  );
  self.clients.claim();
});

self.addEventListener('fetch', (event) => {
  const url = new URL(event.request.url);

  // Skip SignalR hubs, WebSockets, POST/PUT/DELETE requests, and API calls from cache
  if (
    event.request.method !== 'GET' ||
    url.pathname.startsWith('/hubs/') ||
    url.pathname.startsWith('/api/') ||
    url.pathname.includes('signalr')
  ) {
    return;
  }

  // Network-first strategy with cache fallback and offline page for navigations
  event.respondWith(
    fetch(event.request)
      .then((networkResponse) => {
        if (
          networkResponse &&
          networkResponse.status === 200 &&
          (url.pathname.endsWith('.css') ||
            url.pathname.endsWith('.js') ||
            url.pathname.endsWith('.png') ||
            url.pathname.endsWith('.svg') ||
            url.pathname.endsWith('.woff2'))
        ) {
          const responseClone = networkResponse.clone();
          caches.open(CACHE_NAME).then((cache) => {
            cache.put(event.request, responseClone);
          });
        }
        return networkResponse;
      })
      .catch(() => {
        return caches.match(event.request).then((cachedResponse) => {
          if (cachedResponse) {
            return cachedResponse;
          }
          if (event.request.mode === 'navigate') {
            return caches.match('/offline.html');
          }
          return null;
        });
      })
  );
});

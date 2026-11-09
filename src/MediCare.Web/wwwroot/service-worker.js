// MediCare Progressive Web App (PWA) Service Worker Compatibility Shim
// Automatically imports and executes the canonical enterprise service worker (/sw.js)
// to ensure zero disruption for clients previously cached under /service-worker.js.
importScripts('/sw.js');

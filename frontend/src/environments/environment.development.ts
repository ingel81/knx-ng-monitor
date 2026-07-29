// Development environment
export const environment = {
  production: false,
  // Relativ + Dev-Proxy (proxy.conf.json -> :8080), damit der Dev-Server auch
  // über die LAN-IP erreichbar ist (echtes Handy testen). Absolute localhost-
  // URLs würden dort auf das Telefon selbst zeigen.
  apiUrl: '/api',
  hubUrl: '/hubs'
};

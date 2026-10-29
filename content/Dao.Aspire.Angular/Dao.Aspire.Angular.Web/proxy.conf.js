const apiUrl =
  process.env['services__api__https__0'] ||
  process.env['services__api__http__0'] ||
  'https://localhost:7290';

module.exports = {
  '/api': {
    target: apiUrl,
    secure: false,
//#if (IncludeAuthentication)
    changeOrigin: false,
//#else
    changeOrigin: true,
//#endif
  },
//#if (IncludeAuthentication)
  '/signin-google': { target: apiUrl, secure: false, changeOrigin: false },
  '/connect': { target: apiUrl, secure: false, changeOrigin: false },
  '/.well-known': { target: apiUrl, secure: false, changeOrigin: false },
//#endif
//#if (IncludeSignalR)
  '/hubs': {
    target: apiUrl,
    secure: false,
//#if (IncludeAuthentication)
    changeOrigin: false,
//#else
    changeOrigin: true,
//#endif
    ws: true,
  },
//#endif
};

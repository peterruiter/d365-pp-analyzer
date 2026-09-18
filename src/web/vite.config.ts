import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { resolve } from 'node:path';

export default defineConfig({
  plugins: [react()],

  // The product lives under /app so the root can serve a public microsite. Every asset URL the
  // bundle emits has to carry that prefix, or the page loads and its own JavaScript 404s.
  base: '/app/',

  build: {
    outDir: resolve(__dirname, '../PowerPete.Analyzer.Api/wwwroot/app'),

    // The output lives outside this project, so Vite refuses to clear it unless told to. Left
    // alone it accumulates every bundle ever built: forty of them were shipping inside the
    // container image, and grepping the output for a string found whichever stale copy sorted
    // last rather than the one the page actually loads.
    //
    // Now that this is its own directory, emptying it no longer threatens the microsite or the
    // documentation that sit beside it in wwwroot.
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5080',
      '/account': 'http://localhost:5080',
      '/health': 'http://localhost:5080',
    },
  },
});

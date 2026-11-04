import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Builds straight into the web host's wwwroot. In development, API calls are proxied to the .NET app.
export default defineConfig({
  plugins: [react()],
  build: { outDir: '../wwwroot', emptyOutDir: true, sourcemap: false },
  server: { proxy: { '/api': 'http://localhost:5080', '/health': 'http://localhost:5080' } },
});

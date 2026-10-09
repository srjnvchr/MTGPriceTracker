import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    // Forward API calls to the ASP.NET server (see Server/Properties/launchSettings.json).
    proxy: {
      '/api': { target: 'http://localhost:5190', changeOrigin: true },
    },
  },
  build: {
    chunkSizeWarningLimit: 700,
  },
})

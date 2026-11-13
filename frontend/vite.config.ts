import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueDevTools from 'vite-plugin-vue-devtools'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    vue(),
    vueDevTools(),
  ],
  // Atât serverul de dezvoltare, cât și „vite preview” (build-ul de producție) trimit /api către backend.
  preview: {
    proxy: {
      '/api': 'http://localhost:5130',
    },
  },
  server: {
    proxy: {
      '/api': 'http://localhost:5130',
    },
    watch: {
      ignored: ['**/.vs/**'],
    },
  },
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
})

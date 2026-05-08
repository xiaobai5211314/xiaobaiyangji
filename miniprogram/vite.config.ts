import { defineConfig } from 'vite';
import uniPluginModule from '@dcloudio/vite-plugin-uni';
import { fileURLToPath, URL } from 'node:url';

const uni =
  typeof uniPluginModule === 'function'
    ? uniPluginModule
    : (uniPluginModule as unknown as { default: typeof uniPluginModule }).default;

export default defineConfig({
  plugins: [uni()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    }
  }
});

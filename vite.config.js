import { defineConfig } from "vite";

// The app root is src/ (index.html lives there); static assets are in public/.
// Fable compiles .fs files to .fs.js next to the sources, so Vite picks them
// up like any other ES module.
export default defineConfig({
  root: "src",
  publicDir: "../public",
  build: {
    outDir: "../dist",
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    open: false,
  },
});

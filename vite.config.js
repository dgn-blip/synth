import { defineConfig } from "vite";

// The app root is src/ (index.html lives there); static assets are in public/.
// Fable compiles .fs files to .fs.js next to the sources, so Vite picks them
// up like any other ES module.
//
// DEPLOY_BASE lets CI build for a sub-path host (GitHub Pages serves project
// sites at /<repo>/). Locally it stays "/" so dev and preview work unchanged.
export default defineConfig({
  root: "src",
  base: process.env.DEPLOY_BASE || "/",
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

const esbuild = require('esbuild');
const fs = require('fs');
const path = require('path');

// Ensure dist directory exists
const distDir = path.join(__dirname, 'dist');
if (!fs.existsSync(distDir)) {
  fs.mkdirSync(distDir, { recursive: true });
}

// Plugin to replace React imports with global variables
const globalExternalsPlugin = {
  name: 'global-externals',
  setup(build) {
    build.onResolve({ filter: /^react$/ }, args => ({
      path: args.path,
      namespace: 'global-externals'
    }));
    build.onResolve({ filter: /^react-dom$/ }, args => ({
      path: args.path,
      namespace: 'global-externals'
    }));

    build.onLoad({ filter: /.*/, namespace: 'global-externals' }, args => {
      if (args.path === 'react') {
        return {
          contents: 'module.exports = window.React',
          loader: 'js'
        };
      }
      if (args.path === 'react-dom') {
        return {
          contents: 'module.exports = window.ReactDOM',
          loader: 'js'
        };
      }
    });
  }
};

// Build the React component
esbuild.build({
  entryPoints: ['template.tsx'],
  bundle: true,
  outfile: 'dist/viewer.bundle.js',
  format: 'iife',
  globalName: 'ComparisonViewer',
  platform: 'browser',
  target: 'es2020',
  jsx: 'transform',
  jsxFactory: 'React.createElement',
  jsxFragment: 'React.Fragment',
  plugins: [globalExternalsPlugin],
  loader: {
    '.tsx': 'tsx',
    '.ts': 'ts',
  },
  minify: true,
  sourcemap: false,
}).then(() => {
  console.log('✓ Build successful: dist/viewer.bundle.js');
}).catch((error) => {
  console.error('Build failed:', error);
  process.exit(1);
});

import nodeResolve from 'rollup-plugin-node-resolve'
import executable from 'rollup-plugin-executable'
import commonjs from 'rollup-plugin-commonjs';

export default {
  entry: 'src/process-file.js',
  dest: 'dist/resxtract.js',
  format: 'iife',

  sourceMap: true,

  plugins: [
    nodeResolve(),
    commonjs(),
    executable(),
  ],
}

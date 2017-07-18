const { readFileSync, statSync } = require('fs')
const { resolve } = require('path')

const readFile = (file) => {
  const path = resolve(process.cwd(), file)
  try {
    const st = statSync(path)
    if (st.isFile) {
      return readFileSync(path, 'utf8').toString()
    }
  } catch (e) {
    return ''
  }
  return ''
}

module.exports = { readFile }

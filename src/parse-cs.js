const { uniq } = require('ramda')

const parseErrorMessage = (text) => {
  const regex = /ErrorMessage\s*=\s*"([^"]+)"/g

  let match = regex.exec(text)
  let ret = []
  while (match) {
    ret = [
      ...ret,
      match[1].trim()
    ]
    match = regex.exec(text)
  }

  return ret
}

const parseException = (text) => {
  const regex = /new DgmException\(.*_localizer\["([^"]+)"\]\)/g
  let match = regex.exec(text)
  let ret = []
  while (match) {
    ret = [
      ...ret,
      match[1].trim()
    ]
    match = regex.exec(text)
  }

  return ret
}

const parseCs = text => uniq([
  ...parseErrorMessage(text),
  ...parseException(text),
])

module.exports = { parseCs }

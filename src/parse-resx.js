const cheerio = require('cheerio')
const {
  map,
  compose,
} = require('ramda')
const { readFile } = require('./read-file')
const { resolve } = require('path')

const template = readFile(resolve(__dirname, './tpl.resx'))

// string -> $
const parseResx = text =>
  cheerio.load(text || template, {
    xmlMode: true,
    decodeEntities: false,
  })

// $ -> string
const getNameAttr = a => a.attribs.name

// string -> $ -> $
const select = selector => $ => $(selector)

// $ -> array<any>
const toArray = ($) => {
  const ret = []
  $.each((_, item) => ret.push(item))
  return ret
}

// currentData :: $ -> string[]
const currentData = compose(
  map(getNameAttr),
  toArray,
  select('data[name]'))

// const toSelector = d => `data[name="${d}"]`
// removeData :: $ -> string[] -> $
const removeData = $ => data => $('data').each((i, elm) => {
  const name = $(elm).attr('name')
  if (data.indexOf(name) >= 0) {
    $(elm).remove()
  }
})

const toDataElement = name => `<data name="${name}" xml:space="preserve">
  <value>${name}</value>
</data>`

// addData :: $ -> string[] -> $
const addData = $ => data =>
  $('root').append(map(toDataElement)(data))

module.exports = {
  parseResx,
  currentData,
  addData,
  removeData,
}

#! /usr/bin/node

const { compose, difference } = require('ramda')
const { parseCs } = require('./parse-cs')
const { readFile } = require('./read-file')
const { parseResx, currentData, removeData, addData } = require('./parse-resx')
const fs = require('fs')
const { resolve } = require('path')
const meow = require('meow')

const { log } = console

const cli = meow('', {
  alias: {
    o: 'out',
    l: 'locale',
  },
})

let className = cli.input[0]
className = className.match(/\w+/)[0]
const locale = cli.flags.locale || 'vi-VN'
const out = cli.flags.out || '.'
const outPath = resolve(process.cwd(), out, `${className}.${locale}.resx`)
log(outPath)

const getMessages = compose(parseCs, readFile)

const proccesClass = () => {
  const messages = getMessages(`${className}.cs`)
  if (!messages.length) {
    return undefined
  }
  const $ = compose(parseResx, readFile)(outPath)
  const data = currentData($)
  const added = difference(messages, data)
  const removed = difference(data, messages)
  removeData($)(removed)
  addData($)(added)

  log(className)
  added.forEach(n => log(`\t + ${n}`))
  removed.forEach(n => log(`\t - ${n}`))
  return $
}

const xml = $ => $ && $.xml()

const writeFile = (text) => {
  if (!text) {
    return
  }

  const stream = fs.createWriteStream(outPath)
  stream.once('open', () => {
    stream.write(text, 'utf8')
    stream.end()
  })
}


const run = compose(writeFile, xml, proccesClass)

run('')

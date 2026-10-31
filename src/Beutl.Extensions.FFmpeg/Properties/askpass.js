#!/usr/bin/env osascript -l JavaScript

ObjC.import('stdlib')

const app = Application.currentApplication()
app.includeStandardAdditions = true

const result = app.displayDialog('Beutl needs privileged access to complete the current task.\n\nPlease enter your password to allow this:', {
  defaultAnswer: '',
  withIcon: 'caution',
  buttons: ['Cancel', 'Ok'],
  defaultButton: 'Ok',
  hiddenAnswer: true,
})

if (result.buttonReturned === 'Ok') {
  result.textReturned
} else {
  $.exit(255)
}

# Automated fix for issue #3728: シンボリックリンク経由の素材で、プロキシの登録を検知できないことがある

#!/bin/bash

cd ~

rm -fR Documents/workspace/microting/eform-backendconfiguration-plugin/eform-client/src/app/plugins/modules/backend-configuration-pn

cp -a Documents/workspace/microting/eform-angular-frontend/eform-client/src/app/plugins/modules/backend-configuration-pn Documents/workspace/microting/eform-backendconfiguration-plugin/eform-client/src/app/plugins/modules/backend-configuration-pn

rm -fR Documents/workspace/microting/eform-backendconfiguration-plugin/eFormAPI/Plugins/BackendConfiguration.Pn

cp -a Documents/workspace/microting/eform-angular-frontend/eFormAPI/Plugins/BackendConfiguration.Pn Documents/workspace/microting/eform-backendconfiguration-plugin/eFormAPI/Plugins/BackendConfiguration.Pn

# Test files rm
rm -fR Documents/workspace/microting/eform-backendconfiguration-plugin/eform-client/e2e/Tests/backend-configuration-settings/
rm -fR Documents/workspace/microting/eform-backendconfiguration-plugin/eform-client/e2e/Tests/backend-configuration-general/
rm -fR Documents/workspace/microting/eform-backendconfiguration-plugin/eform-client/wdio-headless-plugin-step2.conf.js
rm -fR Documents/workspace/microting/eform-angular-frontend/eform-client/e2e/Assets
rm -fR Documents/workspace/microting/eform-backendconfiguration-plugin/eform-client/e2e/Page\ objects/BackendConfiguration

# Test files cp
cp -a Documents/workspace/microting/eform-angular-frontend/eform-client/e2e/Tests/backend-configuration-settings Documents/workspace/microting/eform-backendconfiguration-plugin/eform-client/e2e/Tests/backend-configuration-settings
cp -a Documents/workspace/microting/eform-angular-frontend/eform-client/e2e/Tests/backend-configuration-general Documents/workspace/microting/eform-backendconfiguration-plugin/eform-client/e2e/Tests/backend-configuration-general
cp -a Documents/workspace/microting/eform-angular-frontend/eform-client/e2e/Page\ objects/BackendConfiguration Documents/workspace/microting/eform-backendconfiguration-plugin/eform-client/e2e/Page\ objects/BackendConfiguration
cp -a Documents/workspace/microting/eform-backendconfiguration-plugin/eform-client/e2e/Assets Documents/workspace/microting/eform-angular-frontend/eform-client/e2e/Assets
cp -a Documents/workspace/microting/eform-angular-frontend/eform-client/wdio-headless-plugin-step2a.conf.js Documents/workspace/microting/eform-backendconfiguration-plugin/eform-client/wdio-headless-plugin-step2a.conf.js
cp -a Documents/workspace/microting/eform-angular-frontend/eform-client/wdio-headless-plugin-step2b.conf.js Documents/workspace/microting/eform-backendconfiguration-plugin/eform-client/wdio-headless-plugin-step2b.conf.js
cp -a Documents/workspace/microting/eform-angular-frontend/eform-client/wdio-headless-plugin-step2c.conf.js Documents/workspace/microting/eform-backendconfiguration-plugin/eform-client/wdio-headless-plugin-step2c.conf.js
cp -a Documents/workspace/microting/eform-angular-frontend/eform-client/wdio-headless-plugin-step2d.conf.js Documents/workspace/microting/eform-backendconfiguration-plugin/eform-client/wdio-headless-plugin-step2d.conf.js
cp -a Documents/workspace/microting/eform-angular-frontend/eform-client/wdio-headless-plugin-step2e.conf.js Documents/workspace/microting/eform-backendconfiguration-plugin/eform-client/wdio-headless-plugin-step2e.conf.js
cp -a Documents/workspace/microting/eform-angular-frontend/eform-client/wdio-headless-plugin-step2f.conf.js Documents/workspace/microting/eform-backendconfiguration-plugin/eform-client/wdio-headless-plugin-step2f.conf.js
cp -a Documents/workspace/microting/eform-angular-frontend/eform-client/wdio-headless-plugin-step2g.conf.js Documents/workspace/microting/eform-backendconfiguration-plugin/eform-client/wdio-headless-plugin-step2g.conf.js
cp -a Documents/workspace/microting/eform-angular-frontend/eform-client/wdio-headless-plugin-step2h.conf.js Documents/workspace/microting/eform-backendconfiguration-plugin/eform-client/wdio-headless-plugin-step2h.conf.js
cp -a Documents/workspace/microting/eform-angular-frontend/eform-client/wdio-headless-plugin-step2i.conf.js Documents/workspace/microting/eform-backendconfiguration-plugin/eform-client/wdio-headless-plugin-step2i.conf.js
cp -a Documents/workspace/microting/eform-angular-frontend/eform-client/wdio-headless-plugin-step2j.conf.js Documents/workspace/microting/eform-backendconfiguration-plugin/eform-client/wdio-headless-plugin-step2j.conf.js

# Changelog

## 06/01/2024

### New

- MQTTClient: handles reconnection after disconnection

### Change

- Upgrade packages:
  - **MQTTNet** from *4.0.0-preview3* to *4.3.0.858*
  - **Newtonsoft.Json** from *10.0.3* to *13.0.3*
  - **PlayniteSDK** from *6.2.0* to *6.9.0*
- Metadata: add link to **simeonradivoev** GitHub project
- Settings: 
  - declare variable with the right type
  - add options for notification and show status changed
  - group options
- MQTTClient: use new options to show or not notifications


### Fix

- MQTTClient: `WithTls()` is deprecated. Replaced by `WithTlsOptions`
- MQTTClient: Add an explicit cast for the client declaration

### Log

- *151da59* - feat(MQTTClient): use new settings for displaying message or notifications. Fix can't disconnect after a power resume.
- *663121c* - feat(Settings): group settings in view. Add settings for display (QoL)
- *dc16494* - chore(metdata): add link to GitHub
- *2e32297* - chore csproj file change
- *d3756e8* - chore: restore code in PowerChange method
- *558e3dc* - chore: remove unecessary using
- *2b364d0* - fix: add an explicit cast for CreateMqttClient()
- *892cf6c* - chore: replace deprecated WIthTLS() method by WithTlsOptions()
- *17526d8* - chore: update plugin version
- *98c8a24* - feat: update MQTTnet, Newtonsoft.json and PlayniteSDK packages
- *f92d85f* - feat: handle disconnection after power resume

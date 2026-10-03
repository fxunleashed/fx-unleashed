# Legal texts

One source for every warning and disclaimer . The plugin embeds these files (`Legal.cs`), the website
reads them from here, so the wording never drifts apart.

| File | Where it appears |
|---|---|
| `disclaimer.md` | Plugin About section, turning on Unleashed mode the first time, updater banner, README top, every release's notes, website footer, `NOTICE` |
| `firmware-warning.md` | Plugin Firmware card (with the checkbox, before anything is staged), setup guide step 3, website firmware page |
| `back-to-stock.md` | Plugin "Back to stock" |
| `library-terms.md` | Plugin library install (once), website library and submit pages |

Changing a text changes its hash, and the plugin asks again for the firmware acknowledgement (`FirmwareAck.TextHash`).

# Security

FX Unleashed runs a small web server on your own PC (`127.0.0.1:8899`: the dash designer, the screen mirror and the
website's one-click library install). It listens on the loopback address only, refuses requests from other web pages
(only fxunleashed.com may use the library endpoints, and only by library id), and asks you to confirm every install in
SimHub.

If you find a way around that, or any other security problem, please report it privately: **Security > Report a
vulnerability** on this repository (GitHub's private reporting), not a public issue. You'll get an answer in a few days.

Firmware: this project never contains Simagic's firmware or its encryption key, and never will. If you think something
in the tree or in a release shouldn't be public, report it the same way.

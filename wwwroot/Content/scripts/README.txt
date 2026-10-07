Browser scripts are served locally so the dashboard works in air-gapped
environments. No CDN is contacted at runtime.

Bundled files:
  - jquery-3.6.0.min.js   jQuery 3.6.0
  - signalr.min.js        @microsoft/signalr browser distribution

To refresh signalr.min.js, take the browser build from the npm package
rather than a CDN link:

  npm pack @microsoft/signalr@8.0.7
  # then extract: package/dist/browser/signalr.min.js

Keep both files committed. Views reference them by these exact names, so a
rename requires updating Views/Dashboard/Index.cshtml.

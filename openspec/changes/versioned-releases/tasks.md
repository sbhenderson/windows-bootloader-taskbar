# Tasks

## 1. Single version source

- [x] 1.1 Add the default `Version` (1.0.0) to `Directory.Build.props`
- [x] 1.2 Replace `SetupVersion` with `$(Version)` in the wixproj and `Product.wxs`

## 2. CI

- [x] 2.1 Add `tags: ['v*']` to the push trigger, plus `branches: ['**']` so branch pushes still build
- [x] 2.2 Add the version step (tag regex or `0.0.<run number>`) and pass `-p:Version` to build, publish and package
- [x] 2.3 Add the `release` job (needs build, tag-only, `contents: write`, renamed MSI, `gh release create --generate-notes`)

## 3. Docs and verification

- [x] 3.1 Update README packaging/release sections (copilot-instructions has no versioning content to change)
- [x] 3.2 Push to main: run 15 passed with the version step (dev build `0.0.15`); local `-p:Version` build confirmed the exe stamp
- [x] 3.3 Push tag `v0.1.0`: confirm the release exists with the MSI attached
- [ ] 3.4 Manual on Windows: install `v0.1.0`, then a later build, and confirm the upgrade

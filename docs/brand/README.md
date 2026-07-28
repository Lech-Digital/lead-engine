# Coldstart brand assets

The `coldstart*` identity as standalone assets: Cabinet Grotesk 800, ink `#16192B`,
asterisk in sunrise `#FF6A3D` (same tokens as `wwwroot/css/site.css`).

| File | Use |
|---|---|
| `icon-512.png` / `icon-128.png` | Square icon — Stripe branding (checkout/portal/emails), app icons, avatars. The asterisk risen over a horizon line: the Morning Engine. |
| `logo-light.png` | Wordmark for light backgrounds (transparent PNG). |
| `logo-dark.png` | Wordmark for dark backgrounds (transparent PNG). |
| `icon.html` / `logo.html` | Sources. Rendered with headless Chrome so the real woff2 fonts are used. |

## Regenerate

```bash
CH="/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
"$CH" --headless=new --disable-gpu --allow-file-access-from-files --hide-scrollbars \
  --default-background-color=00000000 --window-size=512,512 --screenshot=icon-512.png icon.html
"$CH" --headless=new --disable-gpu --allow-file-access-from-files --hide-scrollbars \
  --default-background-color=00000000 --window-size=1800,460 --screenshot=logo-light-raw.png logo.html
# then trim the logo render to its content (PIL: im.crop(im.getbbox()) + 24px padding)
```

For the dark wordmark, change `.mark` color `#16192B` → `#E9E7F2` in `logo.html`.

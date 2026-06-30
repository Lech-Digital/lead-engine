# Pitch site

A single, self-contained static page (`index.html`) — no build step, no external assets,
no dependencies. The real numbers and three leads on it come from actual `leadengine` runs.

## Preview locally

```bash
cd site
python3 -m http.server 8000
# open http://localhost:8000
```
(or just double-click `index.html` — it works straight from the filesystem too)

## Before sending it to anyone

Edit `index.html` and replace the two placeholder addresses in the "Send me an email" links:

```
mailto:hello@yourdomain.com   ->   your real address
```

In `cold-email.md`, replace `{{your site url}}` with wherever you host this page.

## Host it (pick one, all free)

- **GitHub Pages** — push this folder to a repo, Settings → Pages → deploy from branch.
  URL: `https://<user>.github.io/<repo>/`
- **Netlify / Cloudflare Pages** — drag the `site/` folder into their dashboard; instant URL.
- **Any static host / S3 bucket** — upload `index.html`.

## Updating the leads shown

The three featured leads live in `index.html` (the `.lead` cards) and in
`STATE.md` they're drawn from `leads.db`. To refresh with newer leads, pull the top
`fit='YES'` rows from `leads.db` and edit the cards.

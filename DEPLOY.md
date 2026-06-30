# Deploy guide

Two things can be "deployed": the **pitch site** (`site/`) and the **engine** (the
Python code). The engine runs fine on your Mac (daily schedule via `scripts/`), so this
guide is mostly about getting the **site online for free**.

## Important: free hosting + keeping the engine private

GitHub Pages is only free on **public** repos. This repo contains your engine code, so:

| Goal | Recommended setup |
|------|-------------------|
| Keep engine code private **and** host the site free | Private repo here + deploy `site/` via **Netlify** (works from a private repo, free) — **recommended** |
| Use GitHub Pages (free) | Put **only** `site/` in a **separate public repo** |
| Simplest, no git at all | **Netlify Drop** — drag the `site/` folder to app.netlify.com/drop |

The secret `.env`, `leads.db`, `*.csv`, and `logs/` are git-ignored and never pushed.

---

## Option A — Netlify (recommended: private code, free site)

1. Push this repo to GitHub (private) — see "Push to GitHub" below.
2. Go to **app.netlify.com** → Add new site → Import from GitHub → pick this repo.
3. Build settings: **no build command**, **publish directory = `site`**.
4. Deploy. You get a free `*.netlify.app` URL; add a custom domain free in Site settings.

*Or skip git entirely:* **app.netlify.com/drop** → drag the `site/` folder → instant URL.

## Option B — GitHub Pages (free, but site repo must be public)

```bash
# from a folder containing ONLY the site:
gh repo create lead-engine-site --public --source=. --push
# then in the repo: Settings → Pages → Deploy from branch → main → / (root)
```
Keep your engine in this private repo; the public one holds just the marketing page.

---

## Push to GitHub (this repo)

After `gh auth login` (use your **private** account, adziusmaster):

```bash
gh repo create lead-engine --private --source=. --remote=origin --push
```

This creates the repo under your account and pushes `main`.

---

## After deploying — update the placeholders

Once you have a domain/email and a live URL:

- `site/index.html` — replace `hello@yourdomain.com` in the two `mailto:` links.
- `site/cold-email.md` — replace `{{your site url}}` and `{{your name}}`.

(Tell Claude your domain + address and it'll do these edits.)

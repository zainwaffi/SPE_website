# PLAN

Two parts: what still needs doing, and how to get the site live.

---

# 1. Areas for improvement

Item 1 is the only outstanding action. Item 2 records what is already good, item 3 what has
been fixed, and item 4 what was reviewed and deliberately kept as it is.

---

### 1. No automated tests

**Issue.** There is no test project in the solution.

**Why it matters.** Blanket coverage would be a poor investment here and I would not recommend
it — the site is small, the committee turns over yearly, and most of the code is markup that
tests would only make harder to change. But two functions are different: they parse **untrusted
input** and feed the result into HTML, they are pure and fast to test, and a plausible future
edit could silently reopen a security hole with no visible symptom.

**The two worth testing.**

`Shared/YouTubeUrl.cs` — `ToEmbedUrl` turns whatever a committee member pasted into an iframe
`src`. Cases that matter:

| Input | Expected |
|---|---|
| `https://www.youtube.com/watch?v=ID` | `.../embed/ID` |
| `youtu.be/ID` | `.../embed/ID` |
| `youtube.com/watch?v=ID` (no scheme) | `.../embed/ID` — the code prepends `https://` |
| `https://www.youtube.com/shorts/ID`, `/live/ID` | `.../embed/ID` |
| an existing `/embed/ID?start=30` | passed through unchanged |
| `https://youtu.be/ID?t=30` | `t=` dropped, not folded into the id |
| `javascript:alert(1)` | `null` — scheme is neither http nor https |
| `https://vimeo.com/123` | `null` — wrong host |
| `https://youtube.com/watch` (no `v=`) | `null` |
| an id containing `/` or `"` | escaped, so it cannot break out of the `src` attribute |

`Shared/MarkdownRenderer.cs` — `IsSafeUrl` is the only thing standing between a committee
member's markdown and script execution in a Team Leader's session. `DisableHtml()` does not
cover it, because `[text](javascript:…)` is valid markdown. Cases that matter:

| Input | Expected |
|---|---|
| `javascript:alert(1)` | rejected |
| ` javascript:alert(1)` (leading space) | rejected — browsers ignore the space |
| `java\tscript:alert(1)` (embedded tab) | rejected — browsers ignore it |
| `JaVaScRiPt:alert(1)` | rejected — comparison is case-insensitive |
| `data:text/html,<script>` | rejected |
| `/page`, `#anchor`, `../x` | allowed — relative |
| `/a:b`, `#a:b` | allowed — the colon is in the path, not a scheme |
| `mailto:x@y.z`, `tel:+44…`, `https://…` | allowed |
| `<script>alert(1)</script>` in the body | escaped to text, not executed |

A third candidate, lower priority: `Shared/UkTime.cs` around the BST boundary — the bug its
comment describes (events vanishing an hour early during summer time) is exactly the kind that
returns silently.

**Suggested setup.**

```bash
dotnet new xunit -o SPE_website.Tests
dotnet add SPE_website.Tests reference SPE_website.csproj
dotnet test
```

This adds xunit as a **test-only** dependency — it is not referenced by the web project and
ships nothing to production.

**What not to test.** Component/page tests via bunit. They are slow to write, break on every
markup tweak, and this markup changes often by design. The value here is entirely in the two
pure functions above.

**Effort.** Half a day for both tables, including project setup.

---

### 2. Things that are genuinely fine

Listed so the sections above are not read as a verdict on the whole codebase.

- **Images.** Masters live outside `wwwroot` and are resized to WebP by an idempotent script;
  every `<img>` carries explicit `width`/`height`, `srcset` where it matters, and
  `loading="lazy"` below the fold. This is better than most sites of this size.
- **The service layer.** `DbContextFactory` rather than a scoped context (correct for Blazor
  Server), short-lived contexts per call, no obvious N+1s in the pages I read.
- **Markdown handling.** `MarkdownRenderer` disables raw HTML, assembles the pipeline extension
  by extension to avoid generic attributes, *and* scheme-checks link URLs. Three separate holes
  closed deliberately, with the reasoning written down.
- **Time handling.** `UkTime` is the right call, and the comment explaining the BST bug it
  fixed is exactly the kind of comment worth keeping.
- **Admin screens on mobile.** Gated behind `DesktopOnly` with a clear explanation. That is a
  reasonable product decision, not a responsiveness defect — the data tables also have
  `overflow-x-auto` for the in-between widths.
- **Content management.** Events, opportunities, courses and tutorials are all created and
  deleted through the UI by the committee, with markdown for the long-form ones. Only the
  static marketing copy needs a developer, and that is now tagged `#UpdateLink`.

---

### 3. Resolved

Found during the review and since fixed. Kept here so a future reader can see what changed and
why, rather than wondering.

- **Instagram embeds no longer wait for the circuit.** The lazy-loader used to be an ES module
  imported over Blazor JS interop from `OnAfterRenderAsync`, so nothing could start loading
  until the SignalR circuit had connected *and* the page had completed its first interactive
  render. It is now a plain-DOM section of `wwwroot/js/ui.js`, the script every page already
  loads with `defer`, driven by the same `MutationObserver` that handles the scroll reveals.

  What that changes: the first post starts fetching as soon as the markup is parsed rather than
  after the circuit handshake; there is one fewer network request (`instagram-embed.js` is
  deleted) and no dynamic `import()`; embeds work even if the circuit is slow or never connects;
  and `EventsPage` loses `IJSRuntime`, `IAsyncDisposable`, `OnAfterRenderAsync` and the
  `JSDisconnectedException` handling that went with them. The behaviour is unchanged — posts
  still activate only within 300px of the viewport, so a page of events still makes one request
  to Instagram rather than dozens. Removed nodes are now unobserved when the list is filtered,
  which the module version did not do.

- **A link to the post while the embed loads.** Each lazy blockquote now contains a
  "View this post on Instagram" link, centred on the shimmer placeholder. Instagram's own
  `embed.js` replaces the blockquote's contents when it processes the post, so the link
  disappears by itself once the real embed arrives — and stays if the embed never loads, which
  is the point. A reader on a slow connection, or one whose browser blocks Meta, always has a
  way through to the post instead of a shimmer that never resolves. This is also Instagram's
  own documented fallback pattern.

- **Two public pages no longer open a circuit at all.** `OpportunitiesPage` and `CoursesPage`
  were `@rendermode InteractiveServer` at page level while being `[AllowAnonymous]`, so every
  anonymous visitor opened a WebSocket and had a server-side render tree allocated — to read
  fixed copy.

  Both are now static SSR, with their one interactive region extracted into a component that
  declares its own render mode: `Features/Opportunities/Components/OpportunityBoard.razor` and
  `Features/Courses/Components/CourseLibrary.razor`. Both sit inside the existing
  `<AuthorizeView>`, so a guest — who was already shown a sign-in prompt rather than the board
  or the library — now renders no interactive component whatsoever.

  Verified against a published build: `/opportunities` and `/courses` emit **zero**
  `Blazor:{"type":"server"}` component markers for an anonymous request, where they previously
  emitted one each. Home and Scholarships are unchanged at zero. All page content still renders.

  `EventsPage` deliberately keeps its circuit. Search-as-you-type, the category chips, sign-up
  and star ratings are core to how a guest uses that page, and converting them to GET forms
  would be a downgrade in exchange for the saving. It now at least no longer pays interop cost
  for the embeds.

- **Duplicate event routes.** `EventsPage` declared `/events`, `/events/upcoming` and
  `/events/past`, all rendering identical content with no filtering behind them. The page is now
  the single `/events` route. The two old URLs are permanent (301) redirects in `Program.cs`
  pointing at `/events#upcoming` and `/events#past`, and the two sections carry matching `id`
  attributes — so the old links still land in the right place, and the deep-linking those routes
  implied now actually works. Verified: both return 301 to the anchored URL.

- **Invisible heading on the "New Event" form.** The heading was `text-white` inside a white
  card, so committee members opening the form saw an unlabelled panel while a screen reader
  announced a heading nobody could see. It is now `text-spe-cobalt`, matching the two sibling
  headings that already sit on white cards — the calendar modal on the same page and "New
  Tutorial" on the tutorials page.

- **Credentials out of the working tree.** `appsettings.json` is now a template with no secrets
  in it, carrying comments that point at user-secrets locally and environment variables in
  production. The file is untracked, so `.gitignore` finally applies to it. See item 4 for what
  is left.

- **Heading structure.** The home page carried five `<h1>`s and the Opportunities page none.
  The home page's four section kickers are now `<p>` (Bursaries and Courses already did this,
  so the site is consistent), and Opportunities' kicker is now its `<h1>`. Verified in the
  rendered HTML: home serves one `<h1>`, Opportunities serves one. Tailwind's Preflight forces
  headings to inherit font size and weight and zeroes margin on `*`, so both changes are
  pixel-identical on screen. This also gives `<FocusOnNavigate Selector="h1">` in `Routes.razor`
  something to focus on Opportunities, which previously left keyboard focus wherever it was.

  Two pages that looked like they had two `<h1>`s each — Attendees and Tutorial Detail — turned
  out to be fine: the pairs sit in mutually exclusive `@if`/`else` branches, so only one is ever
  rendered. An earlier draft of this file listed them as defects; they are not.

- **Missing alt text.** The careers photo on Opportunities had `alt=""` inside a card headed
  "Student chapters". It is content rather than decoration, so it now describes the picture.

- **Social preview and search metadata.** There was none. `Components/Shared/PageMeta.razor` now
  emits the page title, a meta description, a canonical URL, and Open Graph and Twitter card
  tags, and the five public pages pass their own copy and preview image. `/robots.txt` and
  `/sitemap.xml` are generated per request in `Program.cs`, alongside the calendar feed and in
  the same style — they build absolute URLs from the incoming request, so they are correct on
  localhost, on staging and on the live domain with nothing to configure. The members-only
  routes are disallowed and excluded from the sitemap.

  This matters more than search ranking: a link pasted into Instagram, WhatsApp or LinkedIn now
  unfurls with a title, a description and a photo instead of a bare URL.

- **Mockup styling is now on the theme.** The Volunteering, stats and Careers blocks carried 16
  distinct hard-coded hex values across 30 arbitrary Tailwind utilities in one file, plus four
  arbitrary type sizes. All 16 colours are now named `spe-*` tokens in `Styles/input.css`,
  grouped and commented.

  Each colour was measured against the five brand colours in CIE Lab before deciding. **None**
  was within the just-noticeable-difference threshold (ΔE 2.3), so none was folded into an
  existing token — doing so would have visibly shifted the page. The closest four are noted
  inline with their ΔE (`spe-pale-body` 4.1 from `spe-endeavor`, `spe-amber` 5.4 from
  `spe-gold`, `spe-panel-action` 6.1 from `spe-endeavor`, `spe-panel-blue` 7.5 from `spe-blue`)
  so a designer can collapse them deliberately later — now a one-line change each. The colour
  swap was verified two ways: every token resolves to its original hex in the compiled CSS, and
  resolving the new class names back to colours reproduces the committed file's 30 utilities
  exactly, same property, same value, same order.

  The type sizes were then snapped to the nearest step on the Tailwind scale — 11→`text-xs`,
  13→`text-sm`, 15→`text-base`, 22→`text-2xl`. Three of the four sat exactly between two steps,
  so the rule was round-half-up; each lands within 1–2px of the mockup and on a size the rest of
  the site already uses. Spacing and line-heights are still the mockup's own values, because
  those carry the layout.

- **Committee LinkedIn icons.** Briefly changed to render as plain decoration when no URL is
  set, then reverted on request — the anchors are back exactly as they were, ready for the URLs.
  See item 4.

---

### 4. Reviewed and kept as is

Things that were flagged during the review and then deliberately closed without a code change.

- **Credentials in git history.** The live exposure is closed: both credentials were rotated,
  `appsettings.json` is a template and untracked, and user-secrets supplies the real values
  locally. What remains is history — the originals are still readable in five earlier commits.
  That is harmless **only if** both old credentials are genuinely revoked at Supabase and in the
  Google account; removing them from a file does not stop them working. If you want them gone
  from history too, `git filter-repo` rewrites every commit hash and forces everyone to
  re-clone, or start a fresh repository. Not urgent once revoked.

- **Instagram embeds and Google Fonts without a consent banner.** Both send the visitor's IP to
  a third party before any interaction. Accepted as a deliberate decision. Worth knowing the
  embeds are at least lazy — nothing is fetched from Meta until a post scrolls near the
  viewport — and that self-hosting the three fonts would remove a render-blocking cross-origin
  request as a side benefit, if that ever becomes worth half a day.

- **The calendar arrangement is intentional.** `/events/calendar.ics` is generated by the app
  from the Events table, while the "Add SPE Event Calendar" modal points subscribers at the
  chapter's shared Google Calendar. An earlier draft of this file called that a trap. It is a
  deliberate design decision and is closed. Worth knowing when handing over: an event added
  through this site does not reach Google Calendar subscribers by itself, so whoever adds events
  needs to know both places exist.

- **Committee LinkedIn URLs and "Collab With Us".** All nine `TeamMembers` entries have an empty
  LinkedIn URL, so those icons currently render `<a href="">`, which reloads the home page when
  clicked; and the "Collab With Us" block on the events page has a heading and a "Get in touch"
  line with nothing to get in touch through. Both are content the committee will supply, and the
  markup is deliberately left in place ready for it. Paste each URL into the fourth field of the
  matching `TeamMembers` entry in `Home.razor` and the icon becomes a working link.

- **`EventsPage` keeps its circuit.** See item 3 — the trade was judged the wrong way round for
  that page.

- **Arbitrary `text-[11px]` elsewhere.** Six other files (Bursaries, Events, Profile, Member
  Dashboard, Assigned Tasks, Tutorials) use `text-[11px]` for small uppercase labels — 7
  occurrences. Only the Opportunities ones were snapped to `text-xs`, since the rest are a
  separate site-wide idiom rather than part of the mockup. Running the same substitution across
  those six files would be a one-command change if you want the whole site on the scale; it
  would nudge those labels from 11px to 12px.

- **Mockup spacing.** `p-[26px]`, `leading-[1.65]`, `max-w-[580px]` and similar remain the
  mockup's own values. Unlike type sizes these carry the layout, and the theme scale has no
  equivalents that would not move it.

---

# 2. Deployment

The app is a long-running ASP.NET Core server process. It cannot go on static hosting
(Netlify, GitHub Pages, Vercel's static tier) — Blazor Server holds an open SignalR/WebSocket
connection per visitor and keeps that visitor's UI state on the server.

---

## Step 1 — Credentials (already done, confirm before deploying)

This is done: both credentials were rotated, `appsettings.json` is an untracked template, and
user-secrets supplies the real values locally. Two things to confirm rather than redo:

1. The **old** Gmail app password is revoked at Google Account → Security → App passwords, not
   just removed from the file. Removing it from `appsettings.json` does not stop it working,
   and it is still readable in git history.
2. `git status` does not list `appsettings.json`. If it reappears, it has been re-added.

Never put a password back into `appsettings.json`. Production reads environment variables
(step 4); user-secrets only loads in Development.

## Step 2 — Build

```bash
npm install
npm run build:css
npm run build:images        # only if any master in assets/ changed
dotnet publish -c Release -o ./publish
```

`./publish` is self-contained apart from the .NET runtime. Start it with
`dotnet SPE_website.dll`.

## Step 3 — Choose a host

| Option | Cost | Fit |
|---|---|---|
| **Azure App Service (Linux, B1)** | ~£10/mo, free with student credits | WebSockets and sticky sessions ("ARR affinity") are on by default; first-class .NET deploy from GitHub Actions |
| **Render / Railway** | Free tier, ~£5/mo paid | Simple, Docker or buildpack; free tiers idle the process out, which drops every circuit |
| **A university-provided VM** | Usually free | Full control; you own patching, TLS renewal and the reverse proxy |
| **Azure Container Apps** | Scale-to-zero | Overkill here, and scale-to-zero fights Blazor Server's persistent connections |

**Recommendation: Azure App Service on Linux, B1, in a European region (West Europe or UK
South).**

Three reasons. WebSockets and session affinity are configuration toggles rather than
infrastructure work, and Blazor Server needs both. Students and university societies can
usually get Azure credits, which makes the realistic cost zero. And the Supabase database is
already in `eu-west-2` — keeping the app in a European region keeps the round trip to the
database in single-digit milliseconds, which matters more here than usual because every UI
interaction is a server round trip.

Avoid the free tiers on Render and Railway specifically: they sleep an idle process, and waking
it drops every open circuit, which users experience as the "Rejoining the server…" dialog.

## Step 4 — Configure the environment

Set these on the host as application settings / environment variables. Nested keys use a
double underscore.

```
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__DefaultConnection=Host=…;Database=…;Username=…;Password=…;SSL Mode=Require;Trust Server Certificate=true
EmailSettings__SmtpHost=smtp.gmail.com
EmailSettings__SmtpPort=587
EmailSettings__Username=…
EmailSettings__Password=…                # the NEW app password from step 1
EmailSettings__From=…
EmailSettings__FromName=SPE Aberdeen Student Chapter
EmailSettings__ReplyTo=                  # optional; a monitored human address helps deliverability
```

On App Service also enable **WebSockets** and **ARR affinity** under Configuration → General
settings.

No migration step is needed: `db.Database.MigrateAsync()` runs at startup, and the roles and
the bootstrap Team Leader are seeded on the same pass. A fresh empty database is enough.

## Step 5 — Domain and DNS

1. Decide the address. `spe.ausa.org.uk` or similar under the students' association domain is
   worth asking AUSA for — it inherits the university's credibility and costs nothing. A
   separate `.org.uk` is the fallback, about £10/year.
2. At the DNS provider, point the name at the host:
   - **CNAME** `spe` → `<app-name>.azurewebsites.net` for a subdomain, or
   - **A** record → the host's IP, plus the TXT record the host asks for to prove ownership.
3. Add the custom domain in the host's portal, then let it issue a managed TLS certificate
   (App Service Managed Certificate is free and auto-renews).
4. Leave `UseHttpsRedirection` and `UseHsts` as they are — both are already enabled outside
   development.

DNS changes take up to a few hours to propagate; do this a day before you announce the site,
not on the day.

## Step 6 — Check before the first deploy

- [ ] Old Gmail app password confirmed revoked; `appsettings.json` still untracked
- [ ] Bootstrap admin email changed to a chapter-owned address, in **both**
      `Program.cs` (`adminEmail`) and `OpenWaterAuthService.cs` (`FullAccessEmail`)
- [ ] `dotnet publish -c Release` completes with no warnings
- [ ] `wwwroot/tailwind.css` is current — run `npm run build:css` and check `git status` is
      clean, since the committed copy is what a host without npm will serve
- [ ] Environment variables set, including `ASPNETCORE_ENVIRONMENT=Production`
- [ ] WebSockets and session affinity enabled
- [ ] A database backup taken — startup runs migrations against whatever it is pointed at

## Step 7 — Check after it is live

- [ ] Home page loads over HTTPS with the certificate valid, and http:// redirects to it
- [ ] Sign in with a real SPE member email; confirm the role that comes back is right
- [ ] Sign in as the bootstrap Team Leader and open `/admin/members`
- [ ] Assign a task to a test member and confirm the email actually arrives — check the spam
      folder too, since Gmail SMTP from a new IP is often filtered at first
- [ ] Create an event, confirm it appears, then delete it
- [ ] Open the site on a phone: header menu, events list, and the desktop-only notice on
      `/admin/members`
- [ ] Load a page with an Instagram embed and confirm the post renders
- [ ] Check the host's log stream for startup exceptions and for migration output
- [ ] Leave a tab open for ten minutes, then interact — confirm the circuit reconnects rather
      than showing the failure dialog

# Customising notification email content

`Shared/Services/EmailService.cs` is **not** where the content lives. It is just the pipe — it
takes a subject and an HTML body and posts them to SMTP. The wording is written by whoever
calls it.

## Where each email is written

There are four, in two files:

| Email | Location |
|---|---|
| Strike added | `Features/PresidentAdmin/Services/AdminService.cs` lines 58–66 |
| Strike removed | `Features/PresidentAdmin/Services/AdminService.cs` lines 85–91 |
| Task assigned | `Features/PresidentAdmin/Services/AdminService.cs` lines 121–130 |
| Task completed → team leaders | `Features/Tasks/Services/TaskItemService.cs` lines 88–103 |

Each is a raw-string literal. To reword the strike notice, edit it in place — the second
argument is the subject line, the third the body:

```csharp
var notification = await SendNotificationAsync(
    user,
    "Strike Notice — SPE Chapter",          // ← subject
    $"""
     <p>Dear {Encode(user.FullName)},</p>
     <p>A strike has been added to your record. Your current strike count is <strong>{user.StrikeCount}</strong>.</p>
     <p>Please contact the President if you have any questions.</p>
     <p>— SPE University of Aberdeen Chapter</p>
     """);
```

## Three rules when editing

**Wrap any interpolated value in `Encode(...)`.** Names and task titles are member-supplied and
land in an HTML document — a title containing `<` breaks the markup, and worse is possible.
`Encode` is a local helper at the bottom of each file. Literal text you type yourself does not
need it.

**Do not write a plain-text version.** `EmailService.cs` (lines 89–100) derives one from your
HTML automatically and sends both as `multipart/alternative`. Sending HTML alone is a spam
signal, which is why that exists — but it only understands `<p>`, `<br>`, `<div>`, `<h1>`–`<h6>`,
`<li>` and `<a href>`. A `<table>` layout will flatten into an unreadable run-on line.

**Inline any CSS.** Most mail clients strip `<style>` blocks, so use `style="…"` attributes
directly on elements if you want styling. Nothing currently does.

## Sender identity, not body text

`FromName`, `From` and `ReplyTo` come from the `EmailSettings` config section
(`Shared/Models/EmailSettings.cs`), so the display name and reply address change in
configuration rather than code — set them via user secrets or environment variables, not
`appsettings.json`.

---

## Open option: a shared template

If all four are to be restyled, the current shape means repeating any header/footer four times.
A shared template would fix that — a `Layout(string heading, string body)` helper in
`EmailService` that wraps the chapter's branding around whatever each caller passes, so the
wording stays per-message but the styling lives in one place. Not implemented yet.

using System.Text.Encodings.Web;

namespace Api.Endpoints.SignIn;

/// <summary>Server-rendered sign-in page. No inline script or style, so it works under the strict CSP.</summary>
public static class LoginPage
{
    public static string Render(string formFieldName, string token, string returnUrl, string? error, string? ssoName)
    {
        var html = HtmlEncoder.Default;
        var errorBlock = error is null ? "" : $"""<p class="login-error" role="alert">{html.Encode(error)}</p>""";
        var sso = ssoName is null ? ""
            : $"""<p><a class="login-sso" href="/account/login/oidc?returnUrl={UrlEncoder.Default.Encode(returnUrl)}">Sign in with {html.Encode(ssoName)}</a></p>""";
        return $"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>Sign in · SCADA training plant</title>
              <link rel="stylesheet" href="/styles.css">
            </head>
            <body>
              <main class="login">
                <h1>SCADA training plant</h1>
                {errorBlock}
                <form method="post" action="/account/login">
                  <input type="hidden" name="{html.Encode(formFieldName)}" value="{html.Encode(token)}">
                  <input type="hidden" name="returnUrl" value="{html.Encode(returnUrl)}">
                  <label>Username <input name="username" autocomplete="username" required maxlength="64"></label>
                  <label>Password <input name="password" type="password" autocomplete="current-password" required maxlength="256"></label>
                  <button type="submit">Sign in</button>
                </form>
                {sso}
              </main>
            </body>
            </html>
            """;
    }
}

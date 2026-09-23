# Providers

## Provider transports

DCode supports two provider transport modes behind a common provider
interface.

### API providers

API providers connect directly to model APIs.

- credentials are stored and retrieved by DCode.Server
- DCode.Web may submit credentials but must not persist them
- model discovery, connection tests, usage, and errors belong to the provider
  adapter
- the agent runtime depends on the common provider interface, not an API vendor

### Browser providers

Browser providers use Playwright to interact with authenticated web products.

- Playwright runs behind DCode.Server provider adapters
- each provider uses an isolated persistent browser profile
- a provider may have multiple named profiles running concurrently; each profile
  represents one independently authenticated account and owns a separate browser
  process, cookie store, and user-data directory
- DCode.Web manages sessions but contains no provider-specific automation
- visible and headless execution are explicit user choices
- Browser account overflow actions can switch Agent execution between Visible and Headless. The persisted profile setting applies to warm project sessions and future messages; changing it closes the current operational session so it can restart in the selected mode.
- login, session expiry, challenges, and automation errors are reported through
  the common provider status model

## Provider management page

The Providers page is a global application workspace. It is available outside
project focus mode and is organized by provider rather than by transport.

The overview presents one entry for each provider with combined API and browser
status. Opening a provider shows its API connection and browser accounts in the
same provider detail workspace. Transport configuration and persistence remain
separate behind DCode.Server even though the UI groups them together.

Provider detail navigation uses the existing application browser-history state,
so browser Back returns to the provider overview.

New browser profiles are configured in a modal opened from the Browser Accounts
section. The modal collects only the local profile display name. Provider
usernames and passwords are entered only in
the launched browser and must never be collected by the setup form.

Creating a browser profile immediately starts its browser and opens the provider
login flow. The user must not need to find the new profile and launch it as a
separate step. After the browser starts, automatic authentication detection owns
the remainder of the connection flow. The first launch is always visible;
headless execution is only valid after authentication has been captured.

When a visible provider browser is launched from the desktop application,
DCode.Web requests DCode.Host to restore and foreground the Edge window through
the native bridge. Operating-system window activation remains a DCode.Host
responsibility; DCode.Server and provider adapters do not call Windows UI APIs.

DeepSeek Web is the first browser adapter. DCode.Server can create, list, start,
and stop multiple persistent DeepSeek profiles independently. A running browser
does not imply that its user is authenticated; provider-level login detection is
a separate adapter responsibility.

Browser account state distinguishes connection from runtime:

- `connected` means authenticated cookies, local storage, and IndexedDB have
  been captured for the profile and may be restored later
- `running` means a Playwright browser process is currently alive
- a connected account may remain stopped while idle; the agent runtime may
  start it on demand for chat work and stop it afterward

The DeepSeek adapter detects the authenticated chat composer while the visible
browser is running, then snapshots profile storage automatically. After the
snapshot succeeds, DCode.Server closes the login browser and DCode.Web shows a
temporary success toast. Successful login requires no confirmation or popup
dismissal from the user. Removing an account stops its browser and deletes
both its database record and isolated browser profile after explicit
confirmation in the UI.

Browser account cards reserve the primary action for Start chat and the
secondary action for Test. The overflow menu contains Open browser, Reconnect,
Rename, Clear data, and Delete. Actions without backend capability remain
disabled and must not simulate success. Open browser, Test, and Delete are
currently functional; reconnect, rename, and clear-data contracts follow
in later vertical slices. Test restores saved authentication in a temporary
headless browser, verifies the DeepSeek chat composer, and closes the browser
before returning its result.

Start chat creates a persisted local conversation and navigates to the global
Chat workspace with the selected browser profile and conversation carried in
the existing workspace history state. The chat presents that
profile as its provider session; it is not an agent identity. Browser Back
returns to the originating provider detail.

Every DCode chat maps to at most one remote DeepSeek conversation. The remote
conversation ID and URL are stored after the first provider message creates it.
A conversation remains bound to its original browser account and must never be
silently opened through another account.

The Chat workspace owns new-conversation creation. Its modal selects the
provider transport, connected account, and model/default before creating the
local chat. Users do not need to navigate to Providers first. Provider
management is offered only when no eligible connection exists.

## DeepSeek browser chat lifecycle

Sending a message restores the chat's bound browser profile in a headless
Playwright context. The DeepSeek adapter opens the stored remote conversation URL
or starts a new web conversation, submits the message, waits for a stable
assistant response, and returns the response text and current conversation URL.
DCode.Server then atomically saves the user/assistant exchange and remote
conversation binding. The temporary browser closes after each completed or
failed request.

DeepSeek page selectors are private to the adapter because the web product does
not publish a stable automation DOM contract. Selector changes must not leak
into chat storage, the agent runtime, or DCode.Web.

## Project chat tool continuation

Project Chat runs through the common conversation-provider contract and bounded
conversation runner. The runner knows provider ID, transport, account ID, and
an opaque conversation reference; it does not know about Playwright, DeepSeek
selectors, or API transport details.

DeepSeek Web implements that contract by translating the opaque conversation
reference to its persisted conversation URL. Every tool result is sent to that
same URL so the remote conversation continues rather than starting a new chat.
Because DeepSeek Web has no native tool API, strict tool instructions and tool
results appear in the remote web conversation even though DCode hides the
intercepted markup locally. Browser responses are currently request/response,
not streamed, so DCode shows a general running activity followed by the exact
completed tool activities.

# Authenticated Header Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show an authenticated application header with the product name and a working Cognito logout button.

**Architecture:** `App` remains responsible for deciding whether the user is authenticated and wraps authenticated page content with one shared header. The header calls the existing `AuthContext.signOut` operation and keeps only a local pending flag so duplicate logout requests are disabled.

**Tech Stack:** React 19, TypeScript, Vitest, Testing Library, existing shadcn `Button`

---

### Task 1: Add authenticated header behavior

**Files:**
- Create: `frontend/src/App.test.tsx`
- Modify: `frontend/src/App.tsx`

- [x] **Step 1: Write the failing authenticated-header test**

Create `frontend/src/App.test.tsx` with an authenticated `AuthContext`, a test `QueryClient`, and the `/admin` route without a match ID so no API request is needed. Assert that `Bolão MaisBerlim` and `Sair` are visible, click `Sair`, and assert the context operation is called once.

```tsx
it('shows the authenticated header and signs out', async () => {
  const user = userEvent.setup()
  const signOut = vi.fn().mockResolvedValue(undefined)
  window.history.replaceState({}, '', '/admin')

  renderAuthenticatedApp(signOut)

  expect(screen.getByText('Bolão MaisBerlim')).toBeVisible()
  await user.click(screen.getByRole('button', { name: 'Sair' }))
  expect(signOut).toHaveBeenCalledOnce()
})
```

- [x] **Step 2: Run the focused test and verify RED**

Run: `cd frontend && npm run test:run -- src/App.test.tsx`

Expected: FAIL because the authenticated header and `Sair` button do not exist.

- [x] **Step 3: Implement the authenticated app shell**

In `frontend/src/App.tsx`:

- import the existing `Button` component;
- add a `signingOut` state flag;
- call `auth.signOut()` through an async handler with `try/finally`;
- retain the existing early returns for public and unauthenticated states;
- select the authenticated page content into one variable;
- return a sticky header followed by that page content;
- render `Bolão MaisBerlim` on the left and an outlined `Sair` button on the right;
- disable the button while logout is pending.

```tsx
const [signingOut, setSigningOut] = useState(false)

async function handleSignOut() {
  setSigningOut(true)
  try {
    await auth.signOut()
  } finally {
    setSigningOut(false)
  }
}

return (
  <>
    <header className="sticky top-0 z-40 border-b bg-background/95 backdrop-blur">
      <div className="mx-auto flex h-14 w-full max-w-3xl items-center justify-between px-4 sm:px-8">
        <span className="font-semibold">Bolão MaisBerlim</span>
        <Button variant="outline" onClick={handleSignOut} disabled={signingOut}>
          Sair
        </Button>
      </div>
    </header>
    {page}
  </>
)
```

- [x] **Step 4: Run focused and full verification**

Run: `cd frontend && npm run test:run -- src/App.test.tsx`

Expected: the new test passes.

Run: `cd frontend && npm run test:run`

Expected: all frontend unit tests pass.

Run: `cd frontend && npm run lint`

Expected: exit code 0; pre-existing warnings may remain.

Run: `cd frontend && npm run build`

Expected: TypeScript and Vite build complete successfully.

- [x] **Step 5: Commit the implementation**

```bash
git add frontend/src/App.tsx frontend/src/App.test.tsx docs/superpowers/plans/2026-06-29-authenticated-header.md
git commit -m "Add authenticated logout header"
```

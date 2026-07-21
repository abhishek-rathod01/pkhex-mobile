Primary tap-target action button; use for the main action on a screen or in a card. Danger variant for destructive edits.

```jsx
<Button variant="primary" size="md" onClick={save}>Save changes</Button>
<Button variant="secondary" iconLeft={<PlusIcon/>}>Add move</Button>
<Button variant="ghost" size="sm">Cancel</Button>
```

Variants: primary (accent fill), secondary (outline), ghost (transparent), danger. Sizes sm/md/lg. `fullWidth` for sticky bottom bars.

One row of the stat block. Pass `ivMax`/`evMax` per generation — do NOT assume 31/252.

```jsx
<StatBar stat="hp" label="HP" value={167} max={255} iv={31} ev={252} />
<StatBar stat="atk" label="ATK" value={22} iv={15} ev={65535} ivMax={15} evMax={65535} />
```

// Mock party data for the PkhexMobile UI kit (plausible cross-gen values).
window.PKX_PARTY = [
  { id: 1, dex: 6, species: 'Charizard', nick: 'Ignis', level: 76, types: ['fire', 'flying'], item: 'Charcoal', gender: 'M', shiny: true, legality: 'pass' },
  { id: 2, dex: 9, species: 'Blastoise', nick: null, level: 72, types: ['water'], item: 'Mystic Water', gender: 'F', shiny: false, legality: 'pass' },
  { id: 3, dex: 3, species: 'Venusaur', nick: 'Bloom', level: 70, types: ['grass', 'poison'], item: 'Leftovers', gender: 'M', shiny: false, legality: 'pass' },
  { id: 4, dex: 25, species: 'Pikachu', nick: 'Sparks', level: 55, types: ['electric'], item: 'Light Ball', gender: 'F', shiny: true, legality: 'warn' },
  { id: 5, dex: 94, species: 'Gengar', nick: null, level: 68, types: ['ghost', 'poison'], item: 'Spell Tag', gender: 'M', shiny: false, legality: 'fail', flags: 3 },
  { id: 6, dex: 149, species: 'Dragonite', nick: 'Wyvern', level: 80, types: ['dragon', 'flying'], item: 'Lum Berry', gender: 'M', shiny: false, legality: 'pass' },
];

window.PKX_DETAIL = {
  dex: 6, species: 'Charizard', nick: 'Ignis', level: 76, types: ['fire', 'flying'],
  gender: 'M', shiny: true, form: 'Normal', ability: 'Blaze', item: 'Charcoal', nature: 'Modest',
  legality: 'pass',
  main: { ot: 'RED', tid: '123456', sid: '54321', friendship: 220, language: 'ENG', game: 'Scarlet' },
  met: { location: 'Route 8', date: '2024-11-08', ball: 'Ultra Ball', levelMet: 5, egg: false },
  gen: 9,
  stats: [
    { stat: 'hp', label: 'HP', value: 233, base: 78, iv: 31, ev: 4 },
    { stat: 'atk', label: 'ATK', value: 154, base: 84, iv: 31, ev: 0 },
    { stat: 'def', label: 'DEF', value: 168, base: 78, iv: 31, ev: 0 },
    { stat: 'spa', label: 'SPA', value: 236, base: 109, iv: 31, ev: 252 },
    { stat: 'spd', label: 'SPD', value: 178, base: 85, iv: 31, ev: 0 },
    { stat: 'spe', label: 'SPE', value: 199, base: 100, iv: 31, ev: 252 },
  ],
  ivMax: 31, evMax: 252,
  moves: [
    { name: 'Flamethrower', type: 'fire', pp: 15, ppMax: 15 },
    { name: 'Air Slash', type: 'flying', pp: 15, ppMax: 15 },
    { name: 'Dragon Pulse', type: 'dragon', pp: 10, ppMax: 10 },
    { name: 'Roost', type: 'flying', pp: 10, ppMax: 10 },
  ],
  ribbons: ['Champion', 'Effort', 'Best Friends'],
  memories: 'Went on a walk with its Trainer in Mesagoza.',
};

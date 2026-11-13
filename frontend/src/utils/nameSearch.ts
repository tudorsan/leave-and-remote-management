function normalizeName(value: string) {
  return value.normalize('NFD').replace(/\p{M}/gu, '').toLocaleLowerCase('ro')
}

export function matchesName(value: string, query: string) {
  const name = normalizeName(value)
  return normalizeName(query).trim().split(/\s+/).every(word => name.includes(word))
}

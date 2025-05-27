# Profit Based Industry And Office

[Download on Paradox Mods](https://mods.paradoxplaza.com/mods/83825/Windows)

## Introduction
This mod introduces a different AI system for industrial and office buildings in Cities: Skylines II. Instead of basing expansion decisions on product storage, companies now make decisions based on profitability.

## Key Features
- Companies make expansion and contraction decisions based on their profit rather than product storage amount
- Considers material costs when determining profitability
- Sets minimum company headcount to 1/4 of building capacity
- Added system to sell company products to internal demand and outside cities (graph visuals not yet updated)
- Creates more organic growth of companies

## How It Works

### Original In-Game Logic
- Less than 1/4 amount of storage filled with product: increase headcount
- More than 1/2 amount of storage filled with product: decrease headcount
- Minimum company headcount is 5
- Major issue: Office buildings don't sell any of their products

### Modified Logic
- If profit exceeds a threshold: increase headcount
- If profit falls below threshold: decrease headcount
- Output fulfills internal demand with no markup
- Export and import have price markup and profit markdown
- Minimum company headcount set to 1/4 of building capacity

## Effects of the Mod
- Company headcount will settle at slightly lower levels than pre-economy update
- Traffic will increase due to more product movement
- Non-profitable companies will see decreased headcount
- Tax rates have more dramatic impact on company headcount
- Companies now sell products to both the global market and between cities (with inter-city trade being more profitable)

## Performance Note
This mod may initially cause lag in cities with large populations, but the lag will ease off after the updated calculations are applied to most buildings.

## Future Plans
- Further investigate the profit threshold to improve balance
- Create options for users to define thresholds using percentiles

## Acknowledgements
Thanks to @Mimonsi and @Infixo. Your code in GitHub helped me understand how many of the game functions work.

## Related Discussions
- [Tax Calculation Improvement](https://forum.paradoxplaza.com/forum/threads/tax-calculation-improvement.1693123/)
- [Improving Virtual Goods Handling and Resource System Optimization](https://forum.paradoxplaza.com/forum/threads/suggestion-for-improving-virtual-goods-handling-and-resource-system-optimization.1692705/)
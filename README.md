https://mods.paradoxplaza.com/mods/83825/Windows

Profit Based Industry And Office

This mod introduces different AI system for industrial and office buildings in the game.
Key features:

    Companies make expansion and contraction decisions based on their profit rather than product storage amount.
    Considers material costs.
    Set minimum company headcount to 1/4 of building capacity
    Streamlined to focus purely on profit-based workforce scaling after the vanilla cargo handling fix
    More organic growth of company

Logic
The original in game calculation is as follow

    less than 1/4 amount of storage fill with product, increase headcount
    more than 1/2 amount of storage fill will product, decrease headcount
    minimum company headcount is 5

Issue

    Vanilla handled office product sales, but headcount still failed to follow profitability trends

The mod logic

    if the profit is larger than the Threshold it will increase headcount and vise versa
    minimum company headcount to 1/4 of building capacity

Effect of the mod

    you will see company headcount go to a slightly lower level than pre economy update
    traffic will increase
    non profiting company could see decrease headcount
    tax rate could impact company headcount more dramatically (by logic not verify)
    smoother stabilization once companies align headcount to profitability

Future plan

    investigate the threshold further to generate a balance
    create a option for user to define the threshold using percentile

This mod may initial cause lag with huge population cities, but the lag will ease off after sometime as the updated calculation are done to most building.

Thanks to @Mimonsi and @Infixo . your code in github help me a lot in understand how many of the function works.

ads

my suggestion on current issues

https://forum.paradoxplaza.com/forum/threads/tax-calculation-improvement.1693123/

https://forum.paradoxplaza.com/forum/threads/suggestion-for-improving-virtual-goods-handling-and-resource-system-optimization.1692705/


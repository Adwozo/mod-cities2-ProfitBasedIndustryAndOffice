https://mods.paradoxplaza.com/mods/83825/Windows

Profit Based Industry And Office

This mod introduces different AI system for industrial and office buildings in the game.
Key features:

    Companies make expansion and contraction decisions based on their profit rather than product storage amount.
    Considers material costs.
    Set minimum company headcount to 1/4 of building capacity
    Added system to sell company product to internal demand and outside city(graph still don't update as of now)
    More organic growth of company

Logic
The original in game calculation is as follow

    less than 1/4 amount of storage fill with product, increase headcount
    more than 1/2 amount of storage fill will product, decrease headcount
    minimum company headcount is 5

Issue

    Office do not sell any of the product

The mod logic

    if the profit is larger than the Threshold it will increase headcount and vise versa
    output will fulfilled internal demand with no mark up, export and import will have mark up on the price and mark down on the profit
    minimum company headcount to 1/4 of building capacity

Effect of the mod

    you will see company headcount go to a slightly lower level than pre economy update
    traffic will increase
    non profiting company could see decrease headcount
    tax rate could impact company headcount more dramatically (by logic not verify)
    company will now sell product to the global market and intercity with intercity more profitable.

Future plan

    investigate the threshold further to generate a balance
    create a option for user to define the threshold using percentile

This mod may initial cause lag with huge population cities, but the lag will ease off after sometime as the updated calculation are done to most building.

Thanks to @Mimonsi and @Infixo . your code in github help me a lot in understand how many of the function works.

ads

my suggestion on current issues

https://forum.paradoxplaza.com/forum/threads/tax-calculation-improvement.1693123/

https://forum.paradoxplaza.com/forum/threads/suggestion-for-improving-virtual-goods-handling-and-resource-system-optimization.1692705/


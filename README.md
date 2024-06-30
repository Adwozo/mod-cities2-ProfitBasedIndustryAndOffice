This mod introduces different AI system for industrial and office buildings in the game. Key features include:

    Companies make expansion and contraction decisions based on their profit-to-worth ratio rather than product storage amount.
    Considers material costs.
    Includes safeguards for very small companies.
    Set minimum company headcount to 1/4 of building capacity
    Added system to sell company product to outside city (graph still dont update as of now)

Logic

The original in game calculation is as follow

    less than 1/4 amount of storage fill with product, increase headcount
    more than 1/2 amount of storage fill will product, decrease headcount
    minimum company headcount is 5

Issue

    Office do not sell any of the product

This mod modify the calculation logic

The profit-to-worth ratio (PTW) is equal to

    for industry (cash reserves - material cost)/company total worth
    for office cash reserves/company total worth

The mod logic

    if the PTW is larger than the Threshold it will increase headcount and vise versa
    if the company total worth is smaller than the small company threshold headcount will stay at full
    minimum company headcount to 1/4 of building capacity

Effect of the mod

    you will see company headcount go to a slightly lower level than pre economy update
    traffic will increase
    non profiting company could see decrease headcount
    tax rate could impact company headcount more dramatically (by logic not verify)
    company will now sell product to the global market. (cant do inter city transaction yet)

Future plan

    investigate the threshold further to generate a balance
    create a option for user to define the threshold using percentile
    small company and threshold would be better as a curve so set as a equation


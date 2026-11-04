# Customs.svg (tarkov.dev)

The Customs GPS underlay (`Maps/images/customs.png`) is a raster of the tarkov.dev Customs map.

- URL: https://raw.githubusercontent.com/the-hideout/tarkov-dev-svg-maps/master/Customs.svg
- Also: https://assets.tarkov.dev/maps/svg/Customs.svg · https://tarkov.dev/map/customs
- Author: Shebuka / [the-hideout/tarkov-dev-svg-maps](https://github.com/the-hideout/tarkov-dev-svg-maps)
- Licence upstream: [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/). The upstream repo notes may restrict in-raid overlay use by default.
- Frame: bounds `x0=698, z0=-307, x1=-372, z1=237`, `coordinateRotation` 180. Same projection as the other GPS underlays: `px=(x-x0)/(x1-x0)*W`, `py=(z-z0)/(z1-z0)*H`.
- Interior floors (`Maps/interiors/`, `Maps/interiors.json`) are crops of the same Customs.svg, one raster per floor. The overlay draws dorm room numbers from that JSON; they are not baked into the images. Boiler has no SVG group and is not included.
- Extract positions: https://json.tarkov.dev/regular/maps (`Maps/customs-extracts.json`). ZB-1011 and ZB-1012 are on the left of the raster; Crossroads, Trailer Park, and RUAF are on the right. Those world coordinates are used as published.

## Labs.svg (tarkov.dev)

The Lab GPS images (`Maps/images/the-lab.png`, `Maps/interiors/labs_*.png`) are rasters of the tarkov.dev Labs map. The Lab is underground, so there is no separate outdoor overview: `the-lab.png` is the colored first floor, and Technical / First / Second are the three floor groups from the SVG.

- URL: https://raw.githubusercontent.com/the-hideout/tarkov-dev-svg-maps/master/Labs.svg
- Also: https://assets.tarkov.dev/maps/svg/Labs.svg · https://tarkov.dev/map/labs
- Author: Shebuka / [the-hideout/tarkov-dev-svg-maps](https://github.com/the-hideout/tarkov-dev-svg-maps)
- Licence upstream: [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/), same permission as Customs below.
- Frame: fitted rect `x0=-305.23, z0=-466.72, x1=-78.1, z1=-187.66`, `coordinateRotation` 270. tarkov.dev does not publish svgBounds for Labs. The fit places about 88% of lock positions on the room fills, so markers can sit a few metres off. Image top-left is `(x0, z0)` and bottom-right is `(x1, z1)`.
- Floors follow world Y: below -0.9 Technical, below 3 First, otherwise Second. Title-bar buttons are T / 1 / 2.
- Locked rooms are painted in the keycard color on the floor PNGs. The legend is `Maps/the-lab-keycards.json` (blue, green, black, red, violet, yellow, residential, blue marking, manager, weapon testing, arsenal, unknown). Red and violet are different rooms on the second floor. Arsenal is a low-confidence room and is kept as painted. The unidentified key is a gray marker with no room fill. Native SVG zone fills (olive, blue, cyan) were muted to floor gray and are not keycards.
- Extract positions: https://json.tarkov.dev/regular/maps (`Maps/the-lab-extracts.json`), English and Russian names from `maps_en` / `maps_ru`. An extract is drawn on the floor its Y selects.

## Factory.svg (tarkov.dev)

The Factory GPS images (`Maps/images/factory.png`, `Maps/interiors/factory_*.png`) are rasters of the tarkov.dev Factory map. Factory is an indoor complex. `factory.png` is the overview, and it is the same picture as the ground floor. Basement, ground, second, and third are the four floor groups from the SVG. Until a position arrives the window shows the overview; after that it stays on the floor plan.

- URL: https://raw.githubusercontent.com/the-hideout/tarkov-dev-svg-maps/master/Factory.svg
- Also: https://assets.tarkov.dev/maps/svg/Factory.svg · https://tarkov.dev/map/factory
- Author: Shebuka / [the-hideout/tarkov-dev-svg-maps](https://github.com/the-hideout/tarkov-dev-svg-maps)
- Licence upstream: [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/), same permission as Customs below.
- Frame: published bounds `[[77,-64.5],[-65.5,67.4]]`, `coordinateRotation` 90. Image top-left is `x=77, z=67.4` and bottom-right is `x=-65.5, z=-64.5`. `u=(67.4-z)/131.9`, `v=(77-x)/142.5`.
- Floors follow world Y: below -1 basement, below 3 ground, below 6 second, otherwise third. Title-bar buttons are -1 / 1 / 2 / 3. A button holds until height enters another band. All four floors share this frame, so switching floors does not reset zoom or follow.
- Locks are door markers. Factory.svg has no `.locked` room fills. The legend is `Maps/factory-keycards.json`: Factory emergency exit key (orange `#ff8c1a`, three doors) and TerraGroup storage room keycard (violet `#b05cff`, one basement door). Stairs stay yellow on the raster and are not a key.
- Extract positions: https://json.tarkov.dev/regular/maps (`Maps/factory-extracts.json`), English and Russian names from `maps_en` / `maps_ru`. night-factory is an alias of this map. An extract is drawn on the floor its Y selects. Courtyard Gate sits just outside the published frame.

## GroundZero.svg (tarkov.dev)

The Ground Zero GPS images (`Maps/images/ground-zero.png`, `Maps/interiors/groundzero_*.png`) are rasters of the tarkov.dev Ground Zero map. `ground-zero.png` is the ground floor. Underground, ground, second, and third are the floor groups from the SVG. Upper floors are drawn over the ground layer. There is no separate outdoor drawing.

- URL: https://raw.githubusercontent.com/the-hideout/tarkov-dev-svg-maps/master/GroundZero.svg
- Also: https://assets.tarkov.dev/maps/svg/GroundZero.svg · https://tarkov.dev/map/ground-zero
- Author: Shebuka / [the-hideout/tarkov-dev-svg-maps](https://github.com/the-hideout/tarkov-dev-svg-maps)
- Licence upstream: [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/), same permission as Customs below.
- Frame: official tarkov.dev bounds `x0=249, z0=-124, x1=-99, z1=364`, `coordinateRotation` 180. Image top-left is `(x0, z0)` and bottom-right is `(x1, z1)`. The pack measured 296 of 523 loot containers on building pixels (about 57%). Shifting world x by +2 would raise that to 329, but that shift moves the five painted door markers off their pixels. No in-raid spawn check justified the shift, so the official bounds are unchanged.
- Floors follow world Y: below 21 underground, below 28 ground, below 32.3 second, otherwise third. Title-bar buttons are -1 / 1 / 2 / 3. A button holds until height moves into another band. The overview is the ground raster until a position arrives. All four floors share this frame, so switching floors does not reset zoom or follow.
- GroundZero.svg has no locked-room fills. Door locks are colored markers. The legend is `Maps/ground-zero-keycards.json` (parking utility, factory exit, bank register, horse toilet, science office). Five markers are on the map. A second Factory emergency exit key at x=264.95 is outside the frame and is not drawn. The same five locks are published for ground-zero-21.
- Extract positions: https://json.tarkov.dev/regular/maps (`Maps/ground-zero-extracts.json`), English and Russian names from `maps_en` / `maps_ru`, fetched with User-Agent `TarkovOverlay/1.0`. The ground raster paints the captions. A GPS marker is drawn on the floor its Y selects. Scav Bunker (East Gate), UN Roadblock, and Pinewood Basement are outside the frame. ground-zero-21 shares the in-frame extracts and does not publish those three.

## Permission

Telegram **EFT TarkDev**, 2026-11-04. The user asked whether they could use the site and its data for their own EFT program. The reply was yes, and asked that a link to the program be sent later. Screenshot: [permission-eft-tarkdev-telegram.png](permission-eft-tarkdev-telegram.png).

> Здравствуйте, я разрабатываю свою программу для игры EFT. Могу я использовать ваш сайт и его данные для своей разработки?
>
> Здравствуйте. Да конечно)
> Обязательно отправьте нам потом ссылку, возможно мы её подключим к нашей программе помощи новичкам!

## Other overview rasters

Woods, Lighthouse, Shoreline, Reserve, Interchange, Streets of Tarkov, and Terminal use the matching SVG from the same repository, rasterized as the ground overview only (the tarkov.dev base layer, plus any group marked to stay with it). Upper floors are not baked into these images. Factory and Ground Zero are the floor packs above, not these overviews.

- Woods.svg, Lighthouse.svg, Shoreline.svg, Reserve.svg, Interchange.svg, StreetsOfTarkov.svg, Terminal.svg at https://github.com/the-hideout/tarkov-dev-svg-maps
- Author and licence: Shebuka / CC BY-NC-SA 4.0, same permission as Customs.
- Lighthouse, Shoreline, Reserve, Streets of Tarkov, and Terminal use the published tarkov.dev frame. Spawns and extracts sit inside it. Terminal's API list has no extracts.
- Woods extends max Z to `455.58` (published `442`) so RUAF Gate is inside the frame.
- Interchange extends max X to `621.9` (published `598`) so Path to River (Flare) is inside the frame.
- Extract positions: https://json.tarkov.dev/regular/maps, English and Russian names from `maps_en` / `maps_ru`, one `Maps/<id>-extracts.json` per map.

Labyrinth has no SVG in that repository. Its underlay stays the original frame drawing.

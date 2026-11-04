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

## Permission

Telegram **EFT TarkDev**, 2026-11-04. The user asked whether they could use the site and its data for their own EFT program. The reply was yes, and asked that a link to the program be sent later. Screenshot: [permission-eft-tarkdev-telegram.png](permission-eft-tarkdev-telegram.png).

> Здравствуйте, я разрабатываю свою программу для игры EFT. Могу я использовать ваш сайт и его данные для своей разработки?
>
> Здравствуйте. Да конечно)
> Обязательно отправьте нам потом ссылку, возможно мы её подключим к нашей программе помощи новичкам!

Other GPS underlays in this project are original drawings on the published tarkov.dev frames, not SVG rasters. Customs and The Lab are the two rasters from tarkov-dev-svg-maps.

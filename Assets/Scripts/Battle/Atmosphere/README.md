# Battle atmosphere

Battles use fixed dawn lighting at 07:00 with clear weather. The sun, procedural sky and light distance haze provide the dawn palette. There are no lighting or weather controls on the battle screen and no rain rendering or wet powder effects. Restarts preserve this fixed presentation. `SetHour` and `SetWeather` remain compatible entry points and reapply clear dawn.

Troop shaders receive explicit linear sky, horizon and ground ambient colors, so instanced infantry/cavalry remain readable with light probes disabled. Dawn uses stronger horizon/ground fill; the cinema grade keeps ACES and the warm sun with gentler contrast, less desaturation and a lighter vignette. The saved profile and runtime profile receive the same settings.

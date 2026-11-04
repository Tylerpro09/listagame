LLUVIA DE EMBARAZO v1.2.1
WorldBox 0.51.2 (build 719) — NeoModLoader 1.2.0.1

HABILIDAD EXCLUSIVA DEL JUGADOR
- Nueva habilidad en Naturaleza, icono de gota azul.
- Se comporta COMO LA LLUVIA NORMAL de WorldBox:
  * Misma mecánica de pincel, clic mantenido y tamaños de herramienta.
  * MISMAS gotas reales que caen, sprites y animación del poder "rain".
  * Misma física de caída y sonidos de lluvia original.
  * Cada gota moja el suelo / apaga fuego igual que la lluvia normal.
- Efecto adicional: al caer una gota, los adultos próximos (radio 4 casillas)
  reciben el estado de embarazo mágico de WorldBox.
- El nacimiento usa la reproducción nativa sin pareja, siempre que la especie
  y el estado del juego lo permitan.
- No genera nubes ni partículas de lluvia artificiales.
- No aparece naturalmente. Solo se ejecuta al seleccionar esta habilidad
  y pintar en el mapa. La lluvia natural no provoca embarazos.

INSTALACIÓN
1. Coloca esta carpeta en: Worldbox\Mods\PregnancyRain
2. NeoModLoader.dll queda en worldbox_Data\StreamingAssets\mods
3. Habilita Experimental Mode y reinicia el juego.

USO
1. Abre Naturaleza en la barra inferior.
2. Selecciona "Lluvia de Embarazo", icono azul de gota.
3. Pinta o mantén el clic con el tamaño de pincel deseado.
4. Caen las gotas reales de WorldBox. Cerca de donde caigan, los
   habitantes y animales adultos compatibles quedan embarazados.
5. El estado de embarazo tiene duración objetivo de 9 segundos de juego.

LIMITES
- La misma criatura no recibe otro embarazo durante 32 segundos.
- No se aplica a menores, ni criaturas sin sistema de reproducción compatible.
- No afecta a otros poderes ni genera efecto de embarazo en la lluvia normal.
- No modifica Inmortal Absoluto ni Worldfall.
- v1.2.1: corrige FieldAccessException en Actor.data y llamadas internas
  de estados; limitación de 5 búsquedas por segundo y 4 nuevos embarazos
  por búsqueda; una excepción desactiva solo el efecto mágico, nunca la
  lluvia o el juego.
- Un nacimiento puede verse impedido por otras reglas o modificaciones del juego.
- La compilación no sustituye la prueba visual dentro de un mundo.

CÓDIGO FUENTE:
[ruta local omitida]
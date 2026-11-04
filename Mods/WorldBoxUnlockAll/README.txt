DESBLOQUEAR TODO v1.0.0 — WORLDBOX
=====================================
Compatible: WorldBox 0.51.2 (build 719)
Cargador: NeoModLoader 1.2.0.1 (Experimental Mode)

FUNCIONAMIENTO AUTOMATICO
Al abrir WorldBox con este mod instalado:
- Desbloquea TODOS LOS LOGROS LOCALES del progreso de WorldBox.
  No concede logros externos de Steam; estos quedan intactos.
- Desbloquea mediante GameProgress.debugUnlockAll(), el metodo
  NATIVO del juego, las categorias reconocidas de:
  * Rasgos de criaturas
  * Rasgos de culturas, idiomas, clanes, religiones y reinos
  * Rasgos de subespecies
  * Armas, armaduras y otros equipamientos
  * Genes
  * Criaturas del catalogo / enciclopedia
  * Tramas (plots)
- Muestra los rasgos elegibles en el editor de criaturas:
  can_be_given=true y show_in_meta_editor=true.
- No expone los rasgos internos invisibles usados para guardar
  estadísticas de Fusión.
- El progreso se guarda en el archivo propio del juego:
  worldboxProgress, con la API nativa de WorldBox.
- No reemplaza DLL del juego, no modifica mundos guardados, no
  crea personajes ni modifica las estadísticas de criaturas.
- No necesitas pulsar un boton. Tras cargar el juego, abre o
  reabre el editor para ver las opciones desbloqueadas.
- Reiniciar el juego permite ver los desbloqueos persistentes
  desde el inicio si alguna pantalla se abrió antes de terminar.

LIMITES: PREMIUM Y STEAM
- Los poderes/contenidos cuyo acceso depende de una licencia
  premium legítima NO se fuerzan ni se desbloquean sin compra.
- Los logros EXTERNOS de Steam NO se activan, falsifican ni
  reescriben. Solo el progreso interno/local de WorldBox cambia.
- Algunos rasgos ocultos del motor pueden provocar resultados
  extraordinarios si se usan, aunque el mod los muestre.

COMPATIBILIDAD CON LOS OTROS MODS
- Inmortal Absoluto / Fusión
- Imán de Inmortales
- Muro de Luz Infranqueable / Reino Aislado
- Reloj de Arena Infinito
- Lluvia de Embarazo

RESPALDO ANTERIOR
[ruta local omitida]

PARA REVERTIR
1. Cierra WorldBox.
2. Borra la carpeta Mods\WorldBoxUnlockAll.
3. Si quieres recuperar el estado de los desbloqueos ANTERIOR,
   copia el respaldo worldboxProgress al directorio:
   [ruta local omitida]
   (solo con WorldBox cerrado). Advertencia: esto restaurará
   también los descubrimientos/logros del perfil a ese momento.

CODIGO FUENTE
WorldBoxUnlockAll.cs
Validation\WorldBoxUnlockAll.csproj

Este mod se compilo de manera local para tu version de WorldBox.
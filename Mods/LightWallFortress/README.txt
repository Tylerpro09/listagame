MURO DE LUZ INFRANQUEABLE + REINO AISLADO v1.1.0
WorldBox 0.51.2 / build 719 — NeoModLoader 1.2.0.1

MURO DE LUZ MEJORADO (SE CONSERVA TODO LO DE v1.0)
- Usa el mismo Muro de Luz (wall_light) original de WorldBox.
- Sigue siendo dorado, animado y editable mediante las herramientas
  de construccion/demolicion normales del jugador.
- Resistencia elevada a 1 000 000; proteccion frente a calor, frio,
  explosiones y destruccion normal por el motor.
- Bloquea caminatas, saltos, vuelo, fuerzas, cruces diagonales y
  algunos desplazamientos directos. Devuelve al actor al lado original
  si intenta atravesar la pared.
- El iman original y el Iman de Inmortales pueden cruzar de manera
  MANUAL cuando el usuario los usa. Otros personajes no.

NOVEDAD v1.1.0: REINO AISLADO (AUTOMATICO)
- Si encierras al rey de un reino existente dentro de un Muro de Luz,
  su gobierno exterior PODRA independizar al recinto, pero SOLO cuando:
  A) La pared rodee una region SIN escapes y completamente cerrada.
  B) El recinto mida al menos 24 casillas internas y como maximo
     16 000 casillas (limite tecnico de analisis).
  C) Dentro exista una ciudad REAL de WorldBox, con centro y al
     menos 2 residentes vivos, TODOS dentro del muro.
  D) Los edificios de esa ciudad tambien queden DENTRO del muro.
  E) El rey pertenezca a esa ciudad y siga siendo rey del reino viejo.
  F) El reino viejo conserve al menos otra ciudad FUERA del muro.
  G) El cierre permanezca estable por al menos 24 segundos de tiempo
     real, y no se este arrastrando al rey manualmente con un iman.
- En un recinto valido:
  1. El reino original pierde al rey: su sistema de WorldBox prepara
     automaticamente la eleccion del siguiente gobernante exterior.
  2. La ciudad encerrada se separa usando City.makeOwnKingdom.
  3. El mismo personaje se vuelve rey de la nueva ciudad-reino.
  4. Los residentes y edificios de la ciudad cambian de reino mediante
     el codigo NATIVO del juego, no modificando manualmente archivos.
  5. El rey no pierde inmortalidad, fusion, atributos, sexo ni escala.
- NO crea ciudades a partir de nada, no crea reinos con dos individuos
  que no tengan ciudad, ni mueve por si solo a los habitantes.
- NO actua si la ciudad se extiende fuera del muro (residentes,
  edificios o centro exterior), evitando anexiones incorrectas.
- NO hace nada si el reino original no tiene una ciudad fuera:
  no tiene sentido una sucesion exterior sin reino exterior.
- La historia previa del rey sigue existiendo; el reino lo reemplaza
  politicamente, no borra datos historicos del juego.
- El cierre se confirma con un flood-fill conservador en 8 direcciones
  que impide falsos positivos en esquinas y rechaza caminos al borde.
- Revisa como maximo 2 reyes cada 6 segundos reales, para no
  ralentizar mundos grandes.
- Cuando aparece un reino nuevo, el registro indica "REINO AISLADO
  CREATED". WorldBox maneja la historia y sucesion con el motor propio.

IMPORTANTE
- La separacion politica NO es reversible automaticamente al abrir la
  pared. WorldBox tratara a ambos reinos como independientes.
- En partidas con mucho valor, prueba primero en una COPIA del mundo.
- Copia previa de saves y la version anterior del mod:
  [ruta local omitida]
- La version compila, pero la independencia aun necesita prueba
  funcional dentro de una partida con varias ciudades.
- Si el recinto no contiene una ciudad real valida, el mod mantiene
  el confinamiento sin alterar las coronas.
- WorldBox abierto debe guardarse y REINICIARSE para cargar v1.1.
- No cambia las carpetas de otros mods.

INSTALACION
Copiar la carpeta LightWallFortress en Worldbox\Mods.
NeoModLoader 1.2.0.1 y Experimental Mode.

FUENTES EDITABLES
[ruta local omitida]
   LightWallFortress.cs
   IsolatedKingdom.cs
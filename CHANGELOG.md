# Changelog

Formato basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/).
Versionado semántico, con la salvedad de que **la API pública se considera inestable** hasta que
exista un segundo proyecto consumidor: hasta entonces una versión menor puede romper compilación.

El **formato de archivo** no sigue esa salvedad. Desde la versión 1 del contenedor solo se
extiende, nunca se cambia.

## [Sin publicar]

### Añadido
- Esqueleto del paquete: `package.json`, estructura de carpetas y los siete ensamblados
  (`Core`, `Tooling`, `Serialization.Newtonsoft`, `Crypto`, `Unity`, `Unity.Editor`, `Tests`).
- `Core` y `Tooling` declarados sin referencias al motor (`noEngineReferences`).
- `Serialization.Newtonsoft` tras un *version define*: no compila si el paquete de Newtonsoft no
  está instalado.
- Documentos de arquitectura y plan de implementación en `Documentation~/`.

## [0.1.0] — sin publicar

Fase 0 del plan de implementación: andamiaje. Todavía **sin código funcional**.

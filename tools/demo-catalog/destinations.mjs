// Imagen representativa de cada destino (Destination.ImageUrl). files: archivos de Commons elegidos a mano;
// q: búsquedas cuando no hay uno fijado. La atribución queda en destination-images.manifest.json y
// ATTRIBUTIONS-DESTINOS.md.
export const DESTINATION_IMAGES = {
  // ---- Prioridad máxima ----
  'Santa Cruz de la Sierra': { files: ['File:Catedral de Santa Cruz de la Sierra - Bolivia.jpg'], q: ['Plaza 24 de Septiembre Santa Cruz'] },
  'La Paz': { q: ['La Paz Illimani ciudad', 'La Paz Bolivia vista panorámica'] },
  Cochabamba: { files: ['File:Cristo de la Concordia 1.jpg'], q: ['Cochabamba ciudad vista'] },
  Sucre: { files: ['File:Plaza 25 de Mayo (Sucre) 1.jpg'], q: ['Sucre Bolivia ciudad blanca'] },
  Tarija: { files: ['File:Los parrales de uva en el valle de Concepción en tarija.jpg'], q: ['Tarija plaza Luis de Fuentes'] },
  Uyuni: { files: ['File:Reflection on the Salar de Uyuni, bolivia.jpg'], q: ['Salar de Uyuni'] },
  Potosí: { files: ['File:Cerro Rico over Potosí, Bolivia.jpg'], q: ['Potosí Bolivia ciudad'] },
  Oruro: { files: ['File:Ciudad de Oruro, Bolivia.jpg'], q: ['Oruro Bolivia'] },
  Copacabana: { files: ['File:Bolivia - View of Copacabana and Lake Titicaca.jpg'], q: ['Copacabana Bolivia Titicaca'] },
  Coroico: { files: ['File:Coroico nubes y montañas.jpg'], q: ['Coroico Yungas'] },
  Rurrenabaque: { files: ['File:Vista del río que divide Rurrenabaque de San Buenaventura, Bolivia.jpg'], q: ['Rurrenabaque'] },
  Samaipata: { files: ['File:Samaipata, Bolivia, March 2016.jpg'], q: ['Samaipata'] },
  Torotoro: { files: ['File:ToroToro canyon 2017.jpg'], q: ['Torotoro'] },
  'Villa Tunari': { files: ['File:Una bella vista de villa tunari.JPG'], q: ['Villa Tunari'] },
  Tupiza: { files: ['File:Tupiza desde el cerro Corazon de Jesus.jpg'], q: ['Tupiza Bolivia'] },
  // ---- Resto de destinos con catálogo ----
  'El Alto': { q: ['El Alto Bolivia ciudad teleférico', 'El Alto Bolivia'] },
  Quillacollo: { q: ['Quillacollo Bolivia', 'Quillacollo'] },
  'Sipe Sipe': { files: ['File:Amanecer de Sipe Sipe.JPG'], q: ['Sipe Sipe'] },
  Tiquipaya: { q: ['Tiquipaya Cochabamba', 'Tiquipaya'] },
  Bermejo: { q: ['Bermejo Tarija Bolivia', 'Río Bermejo Bolivia'] },
  Villamontes: { files: ['File:Rio pilcomayo villamontes.jpg'], q: ['Villa Montes Bolivia'] },
  Cotoca: { files: ['File:Santuario de la Virgen de Cotoca.JPG'], q: ['Cotoca'] },
  'Santiago del Torno': { files: ['File:Cañón cerca a la cascada del Jardín de las Delicias - El Torno, Bolivia.jpg'], q: ['El Torno Santa Cruz Bolivia'] },
  'San Ignacio de Velasco': { files: ['File:San Ignacio de Velasco 001.JPG'], q: ['San Ignacio de Velasco'] },
  Trinidad: { q: ['Trinidad Beni Bolivia plaza', 'Trinidad Beni'] },
  Riberalta: { q: ['Riberalta catedral', 'Riberalta río Beni'] },
  // ---- Destinos del seed todavía sin experiencias ----
  Cobija: { q: ['Cobija Pando Bolivia', 'Cobija Bolivia'] },
  Guayaramerín: { q: ['Guayaramerín Bolivia', 'Guayaramerín'] },
  Camiri: { q: ['Camiri Bolivia'] },
  // Caranavi es el pueblo del café de los Yungas: sus cafetales SON su contexto turístico. La búsqueda libre
  // había devuelto el espécimen de una polilla recolectada ahí, que no representa nada del destino.
  Caranavi: { files: ['File:Caranavi field lo (4386253945).jpg', 'File:Caranavi GV lo (4387027164).jpg'], q: [] },
  Montero: { q: ['Montero Santa Cruz Bolivia'] },
  Warnes: { q: ['Warnes Santa Cruz Bolivia'] },
  Yacuiba: { q: ['Yacuiba Bolivia'] },
  Villazón: { q: ['Villazón Bolivia'] },
  // La iglesia de San Agustín es la referencia de Viacha; la búsqueda libre había devuelto un grupo de
  // música fotografiado dentro de ella.
  Viacha: { files: ['File:Puerta principal de la iglesia San Agustín de Viacha.jpg'], q: [] }, // sin foto representativa con licencia libre: la app muestra el fallback
  Mizque: { q: ['Mizque Bolivia'] },
  Sacaba: { q: ['Sacaba Cochabamba Bolivia'] },
  Vinto: { q: ['Vinto Cochabamba Bolivia'] },
  // La laguna Ceramil, en vez del cementerio municipal que devolvía la búsqueda.
  Colcapirhua: { files: ['File:LAGUNA CERAMIL COLCAPIRHUA-CBBA.jpg'], q: [] },
  // Sin foto: la búsqueda devolvía la misma imagen satelital del INPE que ya se había rechazado para
  // Ivirgarzama (el municipio de Puerto Villarroel la incluye, y Commons la archiva con los dos nombres).
  // Una vista satelital de la cuenca del Ichilo no es una foto de destino; el fallback de la app es más honesto.
  'Puerto Villarroel': { q: [] },
  // El pueblo visto desde el suelo, en vez de la foto satelital de la cuenca que devolvía la búsqueda.
  // Sin foto: lo único que hay en Commons es una toma satelital del INPE, que no es una imagen de destino.
  // La app muestra su fallback, que es más honesto que una vista aérea de una cuenca fluvial.
  Ivirgarzama: { q: [] },
  Pocoata: { q: ['Pocoata Bolivia'] },
  'San Joaquín': { q: ['San Joaquín Beni Bolivia'] },
  // Sin foto: 'San Lucas' en Commons devuelve Cabo San Lucas, México. Mejor el fallback que otro país.
  'San Lucas': { q: [] }, // sin foto representativa: fallback
  // El monumento del pueblo. La búsqueda libre traía un cementerio de Dos Cabezas, Arizona.
  // Sin foto: la única imagen del pueblo es un retrato vertical de un monumento, y el filtro de calidad lo
  // descarta con razón — una foto apaisada es lo que necesita una tarjeta de destino.
  Cabezas: { q: [] },
  'Villa Yapacaní': { q: ['Yapacaní Bolivia'] },
  Yapacani: { q: ['Yapacaní Santa Cruz'] },
}

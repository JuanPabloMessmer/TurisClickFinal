/** @type {import('tailwindcss').Config} */
module.exports = {
  // NativeWind en web exige 'class' si algo intenta fijar el esquema de color (lo hace la capa de
  // navegación). La app es de un solo tema, así que esto no cambia nada visual: solo evita que el
  // runtime web tire una excepción al arrancar.
  darkMode: 'class',
  content: ['./app/**/*.{ts,tsx}', './src/**/*.{ts,tsx}'],
  presets: [require('nativewind/preset')],
  theme: {
    extend: {
      colors: {
        primary: '#005F73',
        secondary: '#0A9396',
        accent: '#EE9B00',
        background: '#F6F9FA',
        surface: '#FFFFFF',
        ink: '#102A43',
      },
    },
  },
  plugins: [],
}

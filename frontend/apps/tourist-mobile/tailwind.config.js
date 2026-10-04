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
      // Mismos valores que src/theme/colors.ts. Todo token que exista allá tiene que existir acá, o su
      // utilidad no se genera y vuelve a escribirse como literal en cada pantalla (DESIGN.md §2).
      colors: {
        'brand-900': '#06303C',
        'brand-700': '#005F73',
        'brand-500': '#0E7A8E',
        primary: '#005F73',
        secondary: '#0E7A8E',
        accent: '#E08A00',
        'accent-700': '#A85A08',
        background: '#F6F9FA',
        surface: '#FFFFFF',
        ink: '#102A43',
        'ink-muted': '#5B7285',
        border: '#E2E8F0',
        'border-control': '#828E9C',
        'success-fg': '#166534',
        'success-bg': '#DCFCE7',
        'warning-fg': '#92400E',
        'warning-bg': '#FEF3C7',
        'danger-fg': '#991B1B',
        'danger-bg': '#FEE2E2',
        'info-fg': '#1E40AF',
        'info-bg': '#DBEAFE',
      },
      borderRadius: {
        sm: '8px',
        md: '12px',
        lg: '16px',
        xl: '20px',
      },
      fontFamily: {
        // Inter para toda la UI; Newsreader solo para títulos sobre fotografía (DESIGN.md §3).
        //
        // En React Native el peso no se aplica sobre una familia estática: cada peso es su propio
        // archivo y su propia familia. Los nombres son `ui500/600/700` y no `medium/semibold/bold`
        // justamente para NO pisar las utilidades de peso de Tailwind, que las pantallas que todavía
        // no migraron siguen usando con la fuente del sistema.
        sans: ['Inter_400Regular'],
        ui500: ['Inter_500Medium'],
        ui600: ['Inter_600SemiBold'],
        ui700: ['Inter_700Bold'],
        display: ['Newsreader_600SemiBold'],
      },
      fontSize: {
        caption: ['12px', { lineHeight: '16px' }],
        label: ['13px', { lineHeight: '18px' }],
        body: ['15px', { lineHeight: '22px' }],
        heading: ['17px', { lineHeight: '24px' }],
        title: ['22px', { lineHeight: '28px' }],
        display: ['30px', { lineHeight: '34px' }],
      },
    },
  },
  plugins: [],
}

/* USER CODE BEGIN Header */
/**
  ******************************************************************************
  * @file           : main.c
  * @brief          : Main program body
  ******************************************************************************
  * @attention
  *
  * Copyright (c) 2025 STMicroelectronics.
  * All rights reserved.
  *
  * This software is licensed under terms that can be found in the LICENSE file
  * in the root directory of this software component.
  * If no LICENSE file comes with this software, it is provided AS-IS.
  *
  ******************************************************************************
  */
/* USER CODE END Header */
/* Includes ------------------------------------------------------------------*/
#include "main.h"
#include "usb_host.h"

/* Private includes ----------------------------------------------------------*/
/* USER CODE BEGIN Includes */
#include <math.h>
#include "arm_math.h"
//#include "subject1_data.h"
#include <string.h>          // memcpy
#include "arm_const_structs.h"  //use the prebuilt CFFT tables
#include <stdio.h>
//#include "core_cm4.h"   // for displaying data output
#include <stdlib.h>
#ifndef M_PI                 // if pi isnt define M_PI
#define M_PI 3.14159265358979323846
#endif

/* USER CODE END Includes */

/* Private typedef -----------------------------------------------------------*/
/* USER CODE BEGIN PTD */

/* USER CODE END PTD */

/* Private define ------------------------------------------------------------*/
/* USER CODE BEGIN PD */

/* USER CODE END PD */

/* Private macro -------------------------------------------------------------*/
/* USER CODE BEGIN PM */

/* USER CODE END PM */

/* Private variables ---------------------------------------------------------*/
I2C_HandleTypeDef hi2c1;

I2S_HandleTypeDef hi2s3;

SPI_HandleTypeDef hspi1;

UART_HandleTypeDef huart3;

/* USER CODE BEGIN PV */
/* Frame-based processing config */
enum { NFFT = 1024u };              // power-of-two
enum { HOP  = NFFT/2u };            // 50% overlap

/* Bands & thresholds (global) */
static const float RR_LO = 0.10f, RR_HI = 0.50f;   // respiration 6–30 bpm
static const float HR_LO = 0.80f, HR_HI = 2.00f;   // heart 48–120 bpm
static const float SNR_MIN = 6.0f;                 // dB


/* Working buffers (small, fixed size) */
static float win[NFFT];
static float iq_frame[2u * NFFT];
static float mag_spec[NFFT];


volatile float rms_I, rms_Q;
volatile float peak, f_peak_hz;
volatile uint32_t peak_bin;

//**Static
enum { NDATA_MAX = 3000 };               // >= subject1_count (8192 in your data)
static float hb[NDATA_MAX];

//**Live Data Extraction
static float rx_I[NDATA_MAX];
static float rx_Q[NDATA_MAX];
static uint32_t rx_count = 0;
static float rx_fs_hz = 0.0f;

/* USER CODE END PV */

/* Private function prototypes -----------------------------------------------*/
void SystemClock_Config(void);
static void MX_GPIO_Init(void);
static void MX_I2C1_Init(void);
static void MX_I2S3_Init(void);
static void MX_SPI1_Init(void);
static void MX_USART3_UART_Init(void);
void MX_USB_HOST_Process(void);

/* USER CODE BEGIN PFP */

/* USER CODE END PFP */

/* Private user code ---------------------------------------------------------*/
/* USER CODE BEGIN 0 */

//** Heart beat detection filters **

/* ---------- One-pole filters (bilinear) ---------- */
typedef struct { float b0, b1, a1; float x1, y1; } onepole_t;

static inline void onepole_init_lp(onepole_t* s, float fs, float fc) {
  float K = tanf((float)M_PI * fc / fs);
  float a0 = 1.0f + K;
  s->b0 = K / a0; s->b1 = s->b0; s->a1 = (1.0f - K) / a0; s->x1 = s->y1 = 0.0f;
}
static inline void onepole_init_hp(onepole_t* s, float fs, float fc) {
  float K = tanf((float)M_PI * fc / fs);
  float a0 = 1.0f + K;
  s->b0 = 1.0f / a0; s->b1 = -s->b0; s->a1 = (1.0f - K) / a0; s->x1 = s->y1 = 0.0f;
}
static inline float onepole_step(onepole_t* s, float x) {
  float y = s->b0*x + s->b1*s->x1 - s->a1*s->y1;
  s->x1 = x; s->y1 = y; return y;
}



//**Printing Outputting data**
int _write(int file, char *ptr, int len)
{
  (void)file;
  HAL_UART_Transmit(&huart3, (uint8_t *)ptr, len, HAL_MAX_DELAY);
  return len;
}

/*LIVE TESTING BLOCK*/
static HAL_StatusTypeDef uart_recv_exact(uint8_t *buf, uint32_t len)
{
  return HAL_UART_Receive(&huart3, buf, len, HAL_MAX_DELAY);
}

static HAL_StatusTypeDef receive_iq_dataset(void)
{
  uint32_t count = 0;
  float fs = 0.0f;

  if (uart_recv_exact((uint8_t *)&count, sizeof(count)) != HAL_OK) return HAL_ERROR;
  if (uart_recv_exact((uint8_t *)&fs, sizeof(fs)) != HAL_OK) return HAL_ERROR;

  if (count == 0 || count > NDATA_MAX) {
    printf("RX ERROR: invalid count = %lu\r\n", (unsigned long)count);
    return HAL_ERROR;
  }

  for (uint32_t n = 0; n < count; ++n) {
    if (uart_recv_exact((uint8_t *)&rx_I[n], sizeof(float)) != HAL_OK) return HAL_ERROR;
    if (uart_recv_exact((uint8_t *)&rx_Q[n], sizeof(float)) != HAL_OK) return HAL_ERROR;
  }

  rx_count = count;
  rx_fs_hz = fs;

  //Delay for output terminal
  printf("RX OK: count=%lu fs=%.6f\r\n", (unsigned long)rx_count, rx_fs_hz);
  printf("MCU OUTPUT TERMINAL\r\n");

  return HAL_OK;


 /*LIVE TESTING BLOCK*/



}




/* USER CODE END 0 */

/**
  * @brief  The application entry point.
  * @retval int
  */
int main(void)
{

  /* USER CODE BEGIN 1 */

  /* USER CODE END 1 */

  /* MCU Configuration--------------------------------------------------------*/

  /* Reset of all peripherals, Initializes the Flash interface and the Systick. */
  HAL_Init();

  /* USER CODE BEGIN Init */

  /* USER CODE END Init */

  /* Configure the system clock */
  SystemClock_Config();

  /* USER CODE BEGIN SysInit */

  /* USER CODE END SysInit */

  /* Initialize all configured peripherals */



  MX_GPIO_Init();
  MX_USART3_UART_Init();

  MX_I2C1_Init();
  MX_I2S3_Init();
  MX_SPI1_Init();
  /* MX_USB_HOST_Init(); */

  /* USER CODE BEGIN 2 */

  setvbuf(stdout, NULL, _IONBF, 0);   // Enables print lines to appear immediately

  HAL_Delay(100); //Delay to ensure console is not overloaded

/*LIVE TESTING BLOCK*/

  printf("Waiting for IQ dataset over UART...\r\n");

  if (receive_iq_dataset() != HAL_OK) {
      Error_Handler();
  }

  const float fs_hz = rx_fs_hz;

  if (rx_count < NFFT) {
      printf("RX ERROR: count < NFFT\r\n");
      Error_Handler();
  }
/*LIVE TESTING BLOCK*/

  /* Hann window */
  for (uint32_t n = 0; n < NFFT; ++n) {
      win[n] = 0.5f * (1.0f - cosf(2.0f * (float)M_PI * (float)n / (float)(NFFT - 1u)));
  }

  /* RMS over the entire dataset (streaming accumulate) */
  double sumI = 0.0, sumQ = 0.0;
  double s2I  = 0.0, s2Q  = 0.0;
  for (uint32_t n = 0; n < rx_count; ++n) {
    float i = rx_I[n];
    float q = rx_Q[n];
    sumI += i; sumQ += q;
    s2I  += (double)i * (double)i;
    s2Q  += (double)q * (double)q;
  }
  float meanI = (float)(sumI / (double)rx_count);
  float meanQ = (float)(sumQ / (double)rx_count);

  //**IMG_REJ FIX

  /* --- Widely-linear de-imaging calibration: beta --- */
  double S_I2=0, S_Q2=0, S_IQ=0, S_I2mQ2=0;
  for (uint32_t n=0; n<rx_count; ++n) {
    float i = rx_I[n] - meanI;
    float q = rx_Q[n] - meanQ;
    S_I2    += (double)i*i;
    S_Q2    += (double)q*q;
    S_IQ    += (double)i*q;
    S_I2mQ2 += (double)i*i - (double)q*q;
  }
  double r   = (S_I2 + S_Q2) / rx_count;   /* E[|z|^2] */
  double rpR =  S_I2mQ2      / rx_count;   /* Re{E[z^2]} = E[I^2 - Q^2] */
  double rpI =  2.0*S_IQ     / rx_count;   /* Im{E[z^2]} = 2E[IQ] */
  float beta_r = (float)(rpR / (r + 1e-12));
  float beta_i = (float)(rpI / (r + 1e-12));



  //**IMG_REJ FIX


  rms_I = sqrtf((float)(s2I / (double)rx_count));  // RMS of raw signals
  rms_Q = sqrtf((float)(s2Q / (double)rx_count));

  /* === I/Q image-rejection calibration (compute once over the dataset) === */
  double vI = 0.0, vQ = 0.0, cIQ = 0.0;
  for (uint32_t n = 0; n < rx_count; ++n) {
    float i = rx_I[n] - meanI;
    float q = rx_Q[n] - meanQ;
    vI  += (double)i * (double)i;
    vQ  += (double)q * (double)q;
    cIQ += (double)i * (double)q;
  }
  vI  /= (double)rx_count;
  vQ  /= (double)rx_count;
  cIQ /= (double)rx_count;

  /* leakage (I→Q) and gain to equalize I & Q power after orthogonalization */
  float a_leak = (float)(cIQ / (vI + 1e-12));                       // Q ≈ a*I + Q_perp
  float varQperp = (float)(vQ - (cIQ*cIQ) / (vI + 1e-12));
  if (varQperp < 1e-20f) varQperp = 1e-20f;
  float g_q = sqrtf((float)vI / varQperp);                           // scale Q_perp to match I

  /* === end image-rejection calibration === */




  /* === Heart-band time signal for trigger detection (streaming, low-RAM) === */
  const uint32_t N = rx_count;
  if (N > NDATA_MAX) Error_Handler();

  /* unwrap + 0.6–3 Hz band in one pass into hb[] */
  onepole_t hp06, lp3;
  onepole_init_hp(&hp06, fs_hz, 0.6f);
  onepole_init_lp(&lp3 , fs_hz, 3.0f);

  float prev_phase = 0.0f, off = 0.0f;
  double s2hb = 0.0;

  for (uint32_t n = 0; n < N; ++n) {
    float i = rx_I[n] - meanI;
    float q = rx_Q[n] - meanQ;

    float ph = atan2f(q, i);
    float d  = ph - ((n==0) ? ph : prev_phase);
    if (d >  M_PI) off -= 2.0f*(float)M_PI;
    else if (d < -M_PI) off += 2.0f*(float)M_PI;
    prev_phase = ph;

    float v = ph + off;          // unwrapped phase
    v = onepole_step(&hp06, v);  // 0.6 Hz HP
    v = onepole_step(&lp3,  v);  // 3.0 Hz LP

    hb[n] = v;
    s2hb += (double)v * (double)v;  // for RMS
  }
  float hb_rms = sqrtf((float)(s2hb / (double)N));


  /* CSV header for per-frame stream (printed once) */
  /*printf("t_s,f_peak_hz,peak_mag,rr_hz,rr_bpm,hr_hz,hr_bpm,snr_dB,frame_rmsI,frame_rmsQ,img_rej_dB\r\n");*/

  /* Sliding-FFT peak search */
  float best_peak = -1.0f;
  uint32_t best_bin = 0;

  double acc_hr_hz = 0.0; //Heart beat detection
  uint32_t cnt_hr = 0;

  for (uint32_t start = 0; start + NFFT <= rx_count; start += (NFFT - HOP)) {
      const float t_sec      = (float)start / fs_hz;
      const float bin_hz_loc = fs_hz / (float)NFFT;

      /* ---- Per-frame bias removal, IQ orthogonalization, and WL image cancel ---- */
      const float EPS = 1e-12f;

      /* 1) Per-frame means */
      double sI_m = 0.0, sQ_m = 0.0;
      for (uint32_t n = 0; n < NFFT; ++n) { sI_m += rx_I[start+n]; sQ_m += rx_Q[start+n]; }
      float mi = (float)(sI_m / (double)NFFT);
      float mq = (float)(sQ_m / (double)NFFT);

      /* 2) Per-frame covariance terms */
      double S_I2=0.0, S_Q2=0.0, S_IQ=0.0;
      for (uint32_t n = 0; n < NFFT; ++n) {
        float i = rx_I[start + n] - mi;
        float q = rx_Q[start + n] - mq;
        S_I2 += (double)i*i;  S_Q2 += (double)q*q;  S_IQ += (double)i*q;
      }

      /* Gram–Schmidt: remove I->Q leakage, then scale Q to I power */
      float a_per = (float)(S_IQ / (S_I2 + EPS));
      float varQp = (float)(S_Q2 - (S_IQ*S_IQ) / (S_I2 + EPS));
      if (varQp < 1e-20f) varQp = 1e-20f;
      float g_per = sqrtf((float)(S_I2 / varQp));

      /* 3) Per-frame WL beta on the orthogonalized pair */
      double r=0.0, rpR=0.0, rpI=0.0;
      for (uint32_t n = 0; n < NFFT; ++n) {
        float i0 = rx_I[start + n] - mi;
        float q0 = rx_Q[start + n] - mq;
        float i1 = i0;
        float q1 = g_per * (q0 - a_per * i0);
        r   += (double)i1*i1 + (double)q1*q1;
        rpR += (double)i1*i1 - (double)q1*q1;
        rpI += 2.0 * (double)i1*q1;
      }
      float beta_r = (float)(rpR / (r + EPS));
      float beta_i = (float)(rpI / (r + EPS));

      /* 4) Apply cancellation and window -> iq_frame */
      for (uint32_t n = 0; n < NFFT; ++n) {
        float i0 = rx_I[start + n] - mi;
        float q0 = rx_Q[start + n] - mq;
        float i1 = i0;
        float q1 = g_per * (q0 - a_per * i0);

        float re = (1.0f - beta_r) * i1 - beta_i * q1;
        float im = (1.0f + beta_r) * q1 - beta_i * i1;

        float wv = win[n];
        iq_frame[2u*n]     = re * wv;
        iq_frame[2u*n + 1] = im * wv;
      }



      /* FFT -> magnitude */
#if   (NFFT == 512u)
  arm_cfft_f32(&arm_cfft_sR_f32_len512,  iq_frame, 0, 1);
#elif (NFFT == 1024u)
  arm_cfft_f32(&arm_cfft_sR_f32_len1024, iq_frame, 0, 1);
#elif (NFFT == 2048u)
  arm_cfft_f32(&arm_cfft_sR_f32_len2048, iq_frame, 0, 1);
#elif (NFFT == 4096u)
  arm_cfft_f32(&arm_cfft_sR_f32_len4096, iq_frame, 0, 1);
#else
  arm_cfft_instance_f32 S; arm_cfft_init_f32(&S, NFFT); arm_cfft_f32(&S, iq_frame, 0, 1);
#endif
arm_cmplx_mag_f32(iq_frame, mag_spec, NFFT);
mag_spec[0] = 0.0f;
mag_spec[NFFT/2] = 0.0f;

      /* dominant peak */
float local_peak; uint32_t local_bin;
arm_max_f32(mag_spec, NFFT, &local_peak, &local_bin);
int32_t sb = (local_bin <= NFFT/2u) ? (int32_t)local_bin : (int32_t)local_bin - (int32_t)NFFT;
float f_local = (float)sb * bin_hz_loc;

/* Band-limited RR/HR symmetric search */
uint32_t kmax_rr = 0, kmax_hr = 0; float mag_rr = 0.0f, mag_hr = 0.0f;
uint32_t klo_rr = (uint32_t)ceilf(RR_LO / bin_hz_loc);
uint32_t khi_rr = (uint32_t)floorf(RR_HI / bin_hz_loc); if (khi_rr > NFFT/2u) khi_rr = NFFT/2u;
for (uint32_t k = klo_rr; k <= khi_rr; ++k) {
  float m = mag_spec[k] + mag_spec[NFFT - k];
  if (m > mag_rr) { mag_rr = m; kmax_rr = k; }
}
float f_rr = kmax_rr * bin_hz_loc;

uint32_t klo_hr = (uint32_t)ceilf(HR_LO / bin_hz_loc);
uint32_t khi_hr = (uint32_t)floorf(HR_HI / bin_hz_loc); if (khi_hr > NFFT/2u) khi_hr = NFFT/2u;
for (uint32_t k = klo_hr; k <= khi_hr; ++k) {
  float m = mag_spec[k] + mag_spec[NFFT - k];
  if (m > mag_hr) { mag_hr = m; kmax_hr = k; }
}
float f_hr = kmax_hr * bin_hz_loc;

/* accumulate spec HR for summary */
acc_hr_hz += fabsf(f_hr);
cnt_hr++;

/* SNR using the HR bin (if present), excluding ±1 bins */
uint32_t k_ir = (kmax_hr ? kmax_hr : local_bin);
uint32_t lo_ex = (k_ir > 0) ? k_ir - 1 : 0;
uint32_t hi_ex = (k_ir + 1 < NFFT) ? k_ir + 1 : NFFT - 1;
double sum_mag = 0.0;
for (uint32_t k = 0; k < NFFT; ++k) { if (k >= lo_ex && k <= hi_ex) continue; sum_mag += (double)mag_spec[k]; }
float noise_avg = (float)(sum_mag / (double)(NFFT - (hi_ex - lo_ex + 1)));
float snr_dB = 20.0f * log10f((mag_spec[k_ir] + 1e-12f) / (noise_avg + 1e-12f));

/* Frame RMS of raw I/Q (telemetry only) */
double sI = 0.0, sQ = 0.0;
for (uint32_t n = 0; n < NFFT; ++n) { float ii = rx_I[start+n]; float qq = rx_Q[start+n]; sI += ii*ii; sQ += qq*qq; }
float frame_rmsI = sqrtf((float)(sI/(double)NFFT));
float frame_rmsQ = sqrtf((float)(sQ/(double)NFFT));

/* Mirror index */
uint32_t kpos = k_ir;                       // +f bin
uint32_t kneg = (NFFT - (kpos % NFFT)) % NFFT;  // -f bin

/* Robust 3-bin energies around each side */
float epos = mag_spec[kpos];
if (kpos > 0)            epos += mag_spec[kpos-1];
if (kpos + 1 < NFFT)     epos += mag_spec[kpos+1];

float eneg = mag_spec[kneg];
if (kneg > 0)            eneg += mag_spec[kneg-1];
if (kneg + 1 < NFFT)     eneg += mag_spec[kneg+1];

/* Pick MAIN as the larger side, MIRROR as the other one */
float main3  = epos, mir3 = eneg;
uint32_t k_main = kpos,  k_mirr = kneg;
if (eneg > epos) { main3 = eneg; mir3 = epos; k_main = kneg; k_mirr = kpos; }

float img_rej_dB = 20.0f * log10f((main3 + 1e-12f) / (mir3 + 1e-12f));

/* Convert to bpm + simple plausibility */
float rr_bpm = 60.0f * fabsf(f_rr);
float hr_bpm = 60.0f * fabsf(f_hr);



      /* CSV line */
      /*printf("%.3f,%.6f,%.6f,%.6f,%.2f,%.6f,%.2f,%.2f,%.6f,%.6f,%.2f\r\n",
             t_sec, f_local, mag_spec[k_ir],
             f_rr, rr_bpm, f_hr, hr_bpm,
             snr_dB, frame_rmsI, frame_rmsQ, img_rej_dB);*/

      /* Track global best peak/bin for final summary */
      if (mag_spec[k_ir] > best_peak) { best_peak = mag_spec[k_ir]; best_bin = k_ir; }
      /*if (f_abs < 0.05f || f_abs > 3.0f) printf("WARN @%.3fs: f_peak=%.3f Hz outside [0.05,3] Hz\r\n", t_sec, f_local);*/

      /*=== Band Validation ====*/
      /*printf("BANDS: t=%.3f  f_rr=%.3f Hz  f_hr=%.3f Hz\r\n", t_sec, f_rr, f_hr);
            const float IR_MIN = 20.0f;
            const int   IR_GATE = 0;         // 0 = don’t gate
            float f_abs = fabsf(f_local);
            int in_resp  = (f_abs >= RR_LO && f_abs <= RR_HI);
            int in_heart = (f_abs >= HR_LO && f_abs <= HR_HI);
            printf("VALID: t=%.3f  f_peak=%.3f Hz  band=%s:%s  SNR=%.1f dB:%s  IMG_REJ=%.1f dB:INFO\r\n",
                   t_sec, f_local,
                   (in_resp ? "RESP" : (in_heart ? "HEART" : "OUT")),
                   (in_resp || in_heart) ? "PASS" : "FAIL",
                   snr_dB, (snr_dB >= SNR_MIN) ? "PASS" : "WARN",
                   img_rej_dB);*/

  }

  /* === Time-based verification over the whole record === */
  const float VERIFY_WIN_S = 4.0f;   // 4-second analysis window
  const float VERIFY_HOP_S = 1.0f;   // verify every 1 second

  uint32_t verify_win = (uint32_t)(VERIFY_WIN_S * fs_hz + 0.5f);
  uint32_t verify_hop = (uint32_t)(VERIFY_HOP_S * fs_hz + 0.5f);

  if (verify_win > rx_count) verify_win = rx_count;
  if (verify_hop < 1) verify_hop = 1;

  for (uint32_t start_v = 0; start_v + verify_win <= rx_count; start_v += verify_hop) {
      uint32_t stop_v = start_v + verify_win;
      float t_sec = (float)start_v / fs_hz;

      /* --- local mean removal over this verification window --- */
      double sI_m = 0.0, sQ_m = 0.0;
      for (uint32_t n = start_v; n < stop_v; ++n) {
          sI_m += rx_I[n];
          sQ_m += rx_Q[n];
      }
      float mi = (float)(sI_m / (double)verify_win);
      float mq = (float)(sQ_m / (double)verify_win);

      /* --- build complex windowed frame for FFT --- */
      const float EPS = 1e-12f;

      double S_I2 = 0.0, S_Q2 = 0.0, S_IQ = 0.0;
      for (uint32_t n = 0; n < NFFT; ++n) {
          float i0 = 0.0f, q0 = 0.0f;

          if ((start_v + n) < stop_v) {
              i0 = rx_I[start_v + n] - mi;
              q0 = rx_Q[start_v + n] - mq;
          }

          S_I2 += (double)i0 * i0;
          S_Q2 += (double)q0 * q0;
          S_IQ += (double)i0 * q0;
      }

      float a_per = (float)(S_IQ / (S_I2 + EPS));
      float varQp = (float)(S_Q2 - (S_IQ * S_IQ) / (S_I2 + EPS));
      if (varQp < 1e-20f) varQp = 1e-20f;
      float g_per = sqrtf((float)(S_I2 / varQp));

      double r=0.0, rpR=0.0, rpI=0.0;
      for (uint32_t n = 0; n < NFFT; ++n) {
          float i0 = 0.0f, q0 = 0.0f;

          if ((start_v + n) < stop_v) {
              i0 = rx_I[start_v + n] - mi;
              q0 = rx_Q[start_v + n] - mq;
          }

          float i1 = i0;
          float q1 = g_per * (q0 - a_per * i0);

          r   += (double)i1*i1 + (double)q1*q1;
          rpR += (double)i1*i1 - (double)q1*q1;
          rpI += 2.0 * (double)i1*q1;
      }

      float beta_r_v = (float)(rpR / (r + EPS));
      float beta_i_v = (float)(rpI / (r + EPS));

      for (uint32_t n = 0; n < NFFT; ++n) {
          float i0 = 0.0f, q0 = 0.0f;

          if ((start_v + n) < stop_v) {
              i0 = rx_I[start_v + n] - mi;
              q0 = rx_Q[start_v + n] - mq;
          }

          float i1 = i0;
          float q1 = g_per * (q0 - a_per * i0);

          float re = (1.0f - beta_r_v) * i1 - beta_i_v * q1;
          float im = (1.0f + beta_r_v) * q1 - beta_i_v * i1;

          iq_frame[2u*n]     = re * win[n];
          iq_frame[2u*n + 1] = im * win[n];
      }

      /* FFT -> magnitude */
#if   (NFFT == 512u)
      arm_cfft_f32(&arm_cfft_sR_f32_len512,  iq_frame, 0, 1);
#elif (NFFT == 1024u)
      arm_cfft_f32(&arm_cfft_sR_f32_len1024, iq_frame, 0, 1);
#elif (NFFT == 2048u)
      arm_cfft_f32(&arm_cfft_sR_f32_len2048, iq_frame, 0, 1);
#elif (NFFT == 4096u)
      arm_cfft_f32(&arm_cfft_sR_f32_len4096, iq_frame, 0, 1);
#else
      arm_cfft_instance_f32 S;
      arm_cfft_init_f32(&S, NFFT);
      arm_cfft_f32(&S, iq_frame, 0, 1);
#endif

      arm_cmplx_mag_f32(iq_frame, mag_spec, NFFT);
      mag_spec[0] = 0.0f;
      mag_spec[NFFT/2] = 0.0f;

      float bin_hz_loc = fs_hz / (float)NFFT;

      /* RR search */
      uint32_t kmax_rr = 0, kmax_hr = 0;
      float mag_rr = 0.0f, mag_hr = 0.0f;

      uint32_t klo_rr = (uint32_t)ceilf(RR_LO / bin_hz_loc);
      uint32_t khi_rr = (uint32_t)floorf(RR_HI / bin_hz_loc);
      if (khi_rr > NFFT/2u) khi_rr = NFFT/2u;

      for (uint32_t k = klo_rr; k <= khi_rr; ++k) {
          float m = mag_spec[k] + mag_spec[NFFT - k];
          if (m > mag_rr) { mag_rr = m; kmax_rr = k; }
      }
      float f_rr = kmax_rr * bin_hz_loc;

      /* HR search */
      uint32_t klo_hr = (uint32_t)ceilf(HR_LO / bin_hz_loc);
      uint32_t khi_hr = (uint32_t)floorf(HR_HI / bin_hz_loc);
      if (khi_hr > NFFT/2u) khi_hr = NFFT/2u;

      for (uint32_t k = klo_hr; k <= khi_hr; ++k) {
          float m = mag_spec[k] + mag_spec[NFFT - k];
          if (m > mag_hr) { mag_hr = m; kmax_hr = k; }
      }
      float f_hr = kmax_hr * bin_hz_loc;

      /* dominant peak + SNR */
      float local_peak;
      uint32_t local_bin;
      arm_max_f32(mag_spec, NFFT, &local_peak, &local_bin);

      int32_t sb = (local_bin <= NFFT/2u) ? (int32_t)local_bin : (int32_t)local_bin - (int32_t)NFFT;
      float f_local = (float)sb * bin_hz_loc;

      uint32_t k_ir = (kmax_hr ? kmax_hr : local_bin);
      uint32_t lo_ex = (k_ir > 0) ? k_ir - 1 : 0;
      uint32_t hi_ex = (k_ir + 1 < NFFT) ? k_ir + 1 : NFFT - 1;

      double sum_mag = 0.0;
      for (uint32_t k = 0; k < NFFT; ++k) {
          if (k >= lo_ex && k <= hi_ex) continue;
          sum_mag += (double)mag_spec[k];
      }
      float noise_avg = (float)(sum_mag / (double)(NFFT - (hi_ex - lo_ex + 1)));
      float snr_dB = 20.0f * log10f((mag_spec[k_ir] + 1e-12f) / (noise_avg + 1e-12f));

      /* image rejection */
      uint32_t kpos = k_ir;
      uint32_t kneg = (NFFT - (kpos % NFFT)) % NFFT;

      float epos = mag_spec[kpos];
      if (kpos > 0) epos += mag_spec[kpos-1];
      if (kpos + 1 < NFFT) epos += mag_spec[kpos+1];

      float eneg = mag_spec[kneg];
      if (kneg > 0) eneg += mag_spec[kneg-1];
      if (kneg + 1 < NFFT) eneg += mag_spec[kneg+1];

      float main3 = epos, mir3 = eneg;
      if (eneg > epos) { main3 = eneg; mir3 = epos; }

      float img_rej_dB = 20.0f * log10f((main3 + 1e-12f) / (mir3 + 1e-12f));

      /* pass/fail */
      int rr_ok   = (f_rr >= RR_LO && f_rr <= RR_HI);
      int hr_ok   = (f_hr >= HR_LO && f_hr <= HR_HI);
      int snr_ok  = (snr_dB >= SNR_MIN);

      const float IMG_REJ_MIN = 6.0f;
      int img_ok  = (img_rej_dB >= IMG_REJ_MIN);

      printf("VALID: t=%.3f  RR=%s(%.3f Hz)  HR=%s(%.3f Hz)  SNR=%s(%.1f dB)  IMG=%s(%.1f dB)  f_peak=%.3f Hz\r\n",
             t_sec,
             rr_ok  ? "PASS" : "FAIL", f_rr,
             hr_ok  ? "PASS" : "FAIL", f_hr,
             snr_ok ? "PASS" : "WARN", snr_dB,
             img_ok ? "PASS" : "WARN", img_rej_dB,
             f_local);
  }

  /* === Heartbeat trigger detection (hysteretic + slope + refractory) === */
  const float TH_HI = 0.60f * hb_rms;          // arm threshold (tune 0.5–0.8)
  const float TH_LO = 0.50f * TH_HI;           // disarm threshold (hysteresis)
  const float DMIN  = 0.02f * hb_rms;          // minimum positive slope per sample
  const float MIN_RR_s = 0.30f;                // keep as in your validation
  const uint32_t refr  = (uint32_t)(MIN_RR_s * fs_hz + 0.5f);

  uint32_t max_trigs = N / (uint32_t)(MIN_RR_s*fs_hz + 1);
  if (max_trigs < 8) max_trigs = 8;
  uint32_t *trig_idx = (uint32_t*)malloc(max_trigs*sizeof(uint32_t));
  float    *trig_amp = (float   *)malloc(max_trigs*sizeof(float));
  uint32_t ntrig = 0;
  uint32_t last_peak = 0;
  int armed = 1;

  /*printf("TRIG_HDR: t_s,amp\n");*/

  for (uint32_t n = 1; n + 1 < N; ++n) {
    float v  = hb[n];
    float dv = hb[n] - hb[n-1];

    if (armed) {
      if (v >= TH_HI && dv > DMIN && (n - last_peak) >= refr) {
        /* climb to the local maximum to place the trigger at the peak */
        uint32_t p = n;
        while (p + 1 < N && hb[p+1] >= hb[p]) p++;

        /* final lockout check using peak positions */
        if ((p - last_peak) >= refr && ntrig < max_trigs) {
          trig_idx[ntrig] = p;
          trig_amp[ntrig] = hb[p];
          last_peak = p;
          ++ntrig;
          /*printf("TRIG_HR: t=%.3f s, amp=%.3f\n", (float)p/fs_hz, hb[p]);*/
          armed = 0;                         // disarm until we drop below TH_LO
        }
      }
    } else {
      if (v <= TH_LO) armed = 1;             // re-arm with hysteresis
    }
  }

  /* --- Safety prune: remove any residual duplicates closer than refractory --- */
  for (uint32_t k = 1; k < ntrig; ) {
    if (trig_idx[k] - trig_idx[k-1] < refr) {
      /* keep the stronger one */
      uint32_t drop = (trig_amp[k] >= trig_amp[k-1]) ? (k-1) : k;
      for (uint32_t m = drop; m + 1 < ntrig; ++m) {
        trig_idx[m] = trig_idx[m+1];
        trig_amp[m] = trig_amp[m+1];
      }
      --ntrig;
      if (k) --k;                            // recheck after collapsing
    } else {
      ++k;
    }
  }

  /* === Trigger-rate estimates & validations === */
  float trig_bpm = 0.0f;
  float min_rr_s = 1e9f, max_rr_s = 0.0f;
  if (ntrig >= 2) {
    float dur_s = (float)(trig_idx[ntrig-1] - trig_idx[0]) / fs_hz;
    trig_bpm = 60.0f * (float)(ntrig - 1) / (dur_s + 1e-9f);
    for (uint32_t k = 1; k < ntrig; ++k) {
      float rr = (float)(trig_idx[k] - trig_idx[k-1]) / fs_hz;
      if (rr < min_rr_s) min_rr_s = rr;
      if (rr > max_rr_s) max_rr_s = rr;
    }
  }

  float spec_hr_bpm = 0.0f;
  if (cnt_hr > 0) spec_hr_bpm = 60.0f * (float)(acc_hr_hz / (double)cnt_hr);

  /* peak-to-rms of trigger amplitudes */
  float pk = 0.0f;
  for (uint32_t k = 0; k < ntrig; ++k) if (trig_amp[k] > pk) pk = trig_amp[k];
  float p2r_dB = 20.0f * log10f((pk + 1e-12f) / (hb_rms + 1e-12f));

  /* Gates and results */
  const float HR_MIN_BPM = 40.0f, HR_MAX_BPM = 180.0f;
  int enough_cycles = (ntrig >= 3);
  int rate_in_range = (trig_bpm >= HR_MIN_BPM && trig_bpm <= HR_MAX_BPM);
  int refractory_ok = (min_rr_s >= 0.30f - 1e-6f);
  float rel_err = (spec_hr_bpm > 0.1f) ? fabsf(trig_bpm - spec_hr_bpm) / spec_hr_bpm : 1.0f;
  int close_to_spec = (rel_err <= 0.20f);      // ±20%



  /*=== OUTPUT CSV FILE ===*/
  /*
  printf("TRIG_CSV_BEGIN\r\n");
  printf("t_trig_s,amp\r\n");
  for (uint32_t k = 0; k < ntrig; ++k) {
      printf("%.6f,%.6f\r\n", (float)trig_idx[k] / fs_hz, trig_amp[k]);
  }
  printf("TRIG_CSV_END\r\n");

 //Continuous Heart band Waveform for Scan Filter Analysis
  printf("HB_CSV_BEGIN\r\n");
  printf("t_s,hb\r\n");
  for (uint32_t n = 0; n < N; ++n) {
      printf("%.6f,%.6f\r\n", (float)n / fs_hz, hb[n]);
  }
  printf("HB_CSV_END\r\n");
  */

  //** == HeartBeat VALIDATIONS == **

  /* Final peak & frequency */
  peak = best_peak;
  peak_bin = best_bin;

  const float bin_hz = fs_hz / (float)NFFT;
  /* Map 0..NFFT-1 to -Fs/2..+Fs/2 */
  int32_t signed_bin = (best_bin <= NFFT/2u) ? (int32_t)best_bin
                                             : (int32_t)best_bin - (int32_t)NFFT;
  f_peak_hz = (float)signed_bin * bin_hz;

  //**Display key data results**
  printf("Fs = %.1f Hz, NFFT = %lu, HOP = %lu\r\n",
         fs_hz, (unsigned long)NFFT, (unsigned long)HOP);
  printf("RMS: I = %.6f, Q = %.6f\r\n", rms_I, rms_Q);
  printf("Peak: bin = %lu, f_peak = %.3f Hz, |X| = %.6f\r\n",
         (unsigned long)peak_bin, f_peak_hz, peak);

  //**CSV data file
  /*printf("f_Hz,mag\n");
  for (uint32_t k = 0; k < NFFT; ++k) {
      int32_t sb = (k <= NFFT/2u) ? (int32_t)k : (int32_t)k - (int32_t)NFFT;
      float f = (float)sb * bin_hz;
      printf("%.6f,%.6f\n", f, mag_spec[k]);   // paste into MATLAB/Python
  }*/



  /*=== PRINT STATEMENT BLOCK BEGIN ===*/

  /*=== IMG Calibration ====*/
  printf("IMG_CAL: a=%.4f  g=%.4f  (orthogonalize & scale Q)\r\n", a_leak, g_q);
  printf("IMG_CAL2: beta_r=%.5f beta_i=%.5f (widely-linear)\r\n", (double)beta_r, (double)beta_i);

  /*=== Dataset ====*/
  printf("DATASET: count=%lu  meanI=%.6f  meanQ=%.6f  rmsI=%.6f  rmsQ=%.6f\r\n",
         (unsigned long)rx_count, meanI, meanQ, rms_I, rms_Q);

  const float MEAN_FRAC = 0.03f; // mean must be <3% of RMS
  printf("CHECK: count>=NFFT: %s | meanI: %s | meanQ: %s\r\n",
         (rx_count >= NFFT) ? "PASS" : "FAIL",
         (fabsf(meanI) <= MEAN_FRAC * rms_I) ? "PASS" : "FAIL",
         (fabsf(meanQ) <= MEAN_FRAC * rms_Q) ? "PASS" : "FAIL");

  /*=== Heart-beat trigger validation  ====*/
  printf("TRIG_SUM: n=%lu, spec_hr_bpm=%.1f, minRR=%.3f s, maxRR=%.3f s, p2r=%.1f dB\r\n",
         (unsigned long)ntrig, spec_hr_bpm, min_rr_s, max_rr_s, p2r_dB);

  printf("TRIG_CHECK: cycles>=3:%s | rate_in[40,180]:%s | refractory>=0.30s:%s | close_to_spec(±20%%):%s | p2r>=6dB:%s\n",
          enough_cycles ? "PASS":"FAIL",
          rate_in_range ? "PASS":"WARN",
          refractory_ok ? "PASS":"FAIL",
          close_to_spec  ? "PASS":"WARN",
          (p2r_dB >= 6.0f) ? "PASS":"WARN");

  /*===Continuous heart-band waveform===*/
  printf("TRIG_CSV_BEGIN\r\n");
  printf("t_trig_s,amp\r\n");
  for (uint32_t k = 0; k < ntrig; ++k) {
      printf("%.6f,%.6f\r\n", (float)trig_idx[k] / fs_hz, trig_amp[k]);
  }
  printf("TRIG_CSV_END\r\n");

 //Continuous Heart band Waveform for Scan Filter Analysis
  printf("HB_CSV_BEGIN\r\n");
  printf("t_s,hb\r\n");
  for (uint32_t n = 0; n < N; ++n) {
      printf("%.6f,%.6f\r\n", (float)n / fs_hz, hb[n]);
  }
  printf("HB_CSV_END\r\n");


  /*=== PRINT STATEMENT BLOCK END ===*/






/*======================== MCU CODE END ==============================*/




  /*========== MCU & USB-UART CONFIGURATION SETTINGS =========*/


  /* USER CODE END 2 */

  /* Infinite loop */
  /* USER CODE BEGIN WHILE */

  while (1) //NOT USED FOR OFFLINE PROCESSING
  {
    /* USER CODE END WHILE */

    //MX_USB_HOST_Process(); //NOT USED FOR OFFLINE PROCESSING

    /* USER CODE BEGIN 3 */
   }
  /* USER CODE END 3 */
}

/**
  * @brief System Clock Configuration
  * @retval None
  */
void SystemClock_Config(void)
{
  RCC_OscInitTypeDef RCC_OscInitStruct = {0};
  RCC_ClkInitTypeDef RCC_ClkInitStruct = {0};

  /** Configure the main internal regulator output voltage
  */
  __HAL_RCC_PWR_CLK_ENABLE();
  __HAL_PWR_VOLTAGESCALING_CONFIG(PWR_REGULATOR_VOLTAGE_SCALE1);

  /** Initializes the RCC Oscillators according to the specified parameters
  * in the RCC_OscInitTypeDef structure.
  */
  RCC_OscInitStruct.OscillatorType = RCC_OSCILLATORTYPE_HSI;
  RCC_OscInitStruct.HSIState = RCC_HSI_ON;
  RCC_OscInitStruct.HSICalibrationValue = RCC_HSICALIBRATION_DEFAULT;
  RCC_OscInitStruct.PLL.PLLState = RCC_PLL_ON;
  RCC_OscInitStruct.PLL.PLLSource = RCC_PLLSOURCE_HSI;
  RCC_OscInitStruct.PLL.PLLM = 16;
  RCC_OscInitStruct.PLL.PLLN = 336;
  RCC_OscInitStruct.PLL.PLLP = RCC_PLLP_DIV2;
  RCC_OscInitStruct.PLL.PLLQ = 7;
  if (HAL_RCC_OscConfig(&RCC_OscInitStruct) != HAL_OK)
  {
    Error_Handler();
  }

  /** Initializes the CPU, AHB and APB buses clocks
  */
  RCC_ClkInitStruct.ClockType = RCC_CLOCKTYPE_HCLK|RCC_CLOCKTYPE_SYSCLK
                              |RCC_CLOCKTYPE_PCLK1|RCC_CLOCKTYPE_PCLK2;
  RCC_ClkInitStruct.SYSCLKSource = RCC_SYSCLKSOURCE_PLLCLK;
  RCC_ClkInitStruct.AHBCLKDivider = RCC_SYSCLK_DIV1;
  RCC_ClkInitStruct.APB1CLKDivider = RCC_HCLK_DIV4;
  RCC_ClkInitStruct.APB2CLKDivider = RCC_HCLK_DIV2;

  if (HAL_RCC_ClockConfig(&RCC_ClkInitStruct, FLASH_LATENCY_5) != HAL_OK)
  {
    Error_Handler();
  }
}

/**
  * @brief I2C1 Initialization Function
  * @param None
  * @retval None
  */
static void MX_I2C1_Init(void)
{

  /* USER CODE BEGIN I2C1_Init 0 */

  /* USER CODE END I2C1_Init 0 */

  /* USER CODE BEGIN I2C1_Init 1 */

  /* USER CODE END I2C1_Init 1 */
  hi2c1.Instance = I2C1;
  hi2c1.Init.ClockSpeed = 100000;
  hi2c1.Init.DutyCycle = I2C_DUTYCYCLE_2;
  hi2c1.Init.OwnAddress1 = 0;
  hi2c1.Init.AddressingMode = I2C_ADDRESSINGMODE_7BIT;
  hi2c1.Init.DualAddressMode = I2C_DUALADDRESS_DISABLE;
  hi2c1.Init.OwnAddress2 = 0;
  hi2c1.Init.GeneralCallMode = I2C_GENERALCALL_DISABLE;
  hi2c1.Init.NoStretchMode = I2C_NOSTRETCH_DISABLE;
  if (HAL_I2C_Init(&hi2c1) != HAL_OK)
  {
    Error_Handler();
  }
  /* USER CODE BEGIN I2C1_Init 2 */

  /* USER CODE END I2C1_Init 2 */

}

/**
  * @brief I2S3 Initialization Function
  * @param None
  * @retval None
  */
static void MX_I2S3_Init(void)
{

  /* USER CODE BEGIN I2S3_Init 0 */

  /* USER CODE END I2S3_Init 0 */

  /* USER CODE BEGIN I2S3_Init 1 */

  /* USER CODE END I2S3_Init 1 */
  hi2s3.Instance = SPI3;
  hi2s3.Init.Mode = I2S_MODE_MASTER_TX;
  hi2s3.Init.Standard = I2S_STANDARD_PHILIPS;
  hi2s3.Init.DataFormat = I2S_DATAFORMAT_16B;
  hi2s3.Init.MCLKOutput = I2S_MCLKOUTPUT_ENABLE;
  hi2s3.Init.AudioFreq = I2S_AUDIOFREQ_96K;
  hi2s3.Init.CPOL = I2S_CPOL_LOW;
  hi2s3.Init.ClockSource = I2S_CLOCK_PLL;
  hi2s3.Init.FullDuplexMode = I2S_FULLDUPLEXMODE_DISABLE;
  if (HAL_I2S_Init(&hi2s3) != HAL_OK)
  {
    Error_Handler();
  }
  /* USER CODE BEGIN I2S3_Init 2 */

  /* USER CODE END I2S3_Init 2 */

}

/**
  * @brief SPI1 Initialization Function
  * @param None
  * @retval None
  */
static void MX_SPI1_Init(void)
{

  /* USER CODE BEGIN SPI1_Init 0 */

  /* USER CODE END SPI1_Init 0 */

  /* USER CODE BEGIN SPI1_Init 1 */

  /* USER CODE END SPI1_Init 1 */
  /* SPI1 parameter configuration*/
  hspi1.Instance = SPI1;
  hspi1.Init.Mode = SPI_MODE_MASTER;
  hspi1.Init.Direction = SPI_DIRECTION_2LINES;
  hspi1.Init.DataSize = SPI_DATASIZE_8BIT;
  hspi1.Init.CLKPolarity = SPI_POLARITY_LOW;
  hspi1.Init.CLKPhase = SPI_PHASE_1EDGE;
  hspi1.Init.NSS = SPI_NSS_SOFT;
  hspi1.Init.BaudRatePrescaler = SPI_BAUDRATEPRESCALER_2;
  hspi1.Init.FirstBit = SPI_FIRSTBIT_MSB;
  hspi1.Init.TIMode = SPI_TIMODE_DISABLE;
  hspi1.Init.CRCCalculation = SPI_CRCCALCULATION_DISABLE;
  hspi1.Init.CRCPolynomial = 10;
  if (HAL_SPI_Init(&hspi1) != HAL_OK)
  {
    Error_Handler();
  }
  /* USER CODE BEGIN SPI1_Init 2 */

  /* USER CODE END SPI1_Init 2 */

}

/**
  * @brief GPIO Initialization Function
  * @param None
  * @retval None
  */
static void MX_GPIO_Init(void)
{
  GPIO_InitTypeDef GPIO_InitStruct = {0};
  /* USER CODE BEGIN MX_GPIO_Init_1 */

  /* USER CODE END MX_GPIO_Init_1 */

  /* GPIO Ports Clock Enable */
  __HAL_RCC_GPIOE_CLK_ENABLE();
  __HAL_RCC_GPIOC_CLK_ENABLE();
  __HAL_RCC_GPIOH_CLK_ENABLE();
  __HAL_RCC_GPIOA_CLK_ENABLE();
  __HAL_RCC_GPIOB_CLK_ENABLE();
  __HAL_RCC_GPIOD_CLK_ENABLE();

  /*Configure GPIO pin Output Level */
  HAL_GPIO_WritePin(CS_I2C_SPI_GPIO_Port, CS_I2C_SPI_Pin, GPIO_PIN_RESET);

  /*Configure GPIO pin Output Level */
  HAL_GPIO_WritePin(OTG_FS_PowerSwitchOn_GPIO_Port, OTG_FS_PowerSwitchOn_Pin, GPIO_PIN_SET);

  /*Configure GPIO pin Output Level */
  HAL_GPIO_WritePin(GPIOD, LD4_Pin|LD3_Pin|LD5_Pin|LD6_Pin
                          |Audio_RST_Pin, GPIO_PIN_RESET);

  /*Configure GPIO pin : CS_I2C_SPI_Pin */
  GPIO_InitStruct.Pin = CS_I2C_SPI_Pin;
  GPIO_InitStruct.Mode = GPIO_MODE_OUTPUT_PP;
  GPIO_InitStruct.Pull = GPIO_NOPULL;
  GPIO_InitStruct.Speed = GPIO_SPEED_FREQ_LOW;
  HAL_GPIO_Init(CS_I2C_SPI_GPIO_Port, &GPIO_InitStruct);

  /*Configure GPIO pin : OTG_FS_PowerSwitchOn_Pin */
  GPIO_InitStruct.Pin = OTG_FS_PowerSwitchOn_Pin;
  GPIO_InitStruct.Mode = GPIO_MODE_OUTPUT_PP;
  GPIO_InitStruct.Pull = GPIO_NOPULL;
  GPIO_InitStruct.Speed = GPIO_SPEED_FREQ_LOW;
  HAL_GPIO_Init(OTG_FS_PowerSwitchOn_GPIO_Port, &GPIO_InitStruct);

  /*Configure GPIO pin : PDM_OUT_Pin */
  GPIO_InitStruct.Pin = PDM_OUT_Pin;
  GPIO_InitStruct.Mode = GPIO_MODE_AF_PP;
  GPIO_InitStruct.Pull = GPIO_NOPULL;
  GPIO_InitStruct.Speed = GPIO_SPEED_FREQ_LOW;
  GPIO_InitStruct.Alternate = GPIO_AF5_SPI2;
  HAL_GPIO_Init(PDM_OUT_GPIO_Port, &GPIO_InitStruct);

  /*Configure GPIO pin : B1_Pin */
  GPIO_InitStruct.Pin = B1_Pin;
  GPIO_InitStruct.Mode = GPIO_MODE_EVT_RISING;
  GPIO_InitStruct.Pull = GPIO_NOPULL;
  HAL_GPIO_Init(B1_GPIO_Port, &GPIO_InitStruct);

  /*Configure GPIO pin : BOOT1_Pin */
  GPIO_InitStruct.Pin = BOOT1_Pin;
  GPIO_InitStruct.Mode = GPIO_MODE_INPUT;
  GPIO_InitStruct.Pull = GPIO_NOPULL;
  HAL_GPIO_Init(BOOT1_GPIO_Port, &GPIO_InitStruct);

  /*Configure GPIO pin : CLK_IN_Pin */
  GPIO_InitStruct.Pin = CLK_IN_Pin;
  GPIO_InitStruct.Mode = GPIO_MODE_AF_PP;
  GPIO_InitStruct.Pull = GPIO_NOPULL;
  GPIO_InitStruct.Speed = GPIO_SPEED_FREQ_LOW;
  GPIO_InitStruct.Alternate = GPIO_AF5_SPI2;
  HAL_GPIO_Init(CLK_IN_GPIO_Port, &GPIO_InitStruct);

  /*Configure GPIO pins : LD4_Pin LD3_Pin LD5_Pin LD6_Pin
                           Audio_RST_Pin */
  GPIO_InitStruct.Pin = LD4_Pin|LD3_Pin|LD5_Pin|LD6_Pin
                          |Audio_RST_Pin;
  GPIO_InitStruct.Mode = GPIO_MODE_OUTPUT_PP;
  GPIO_InitStruct.Pull = GPIO_NOPULL;
  GPIO_InitStruct.Speed = GPIO_SPEED_FREQ_LOW;
  HAL_GPIO_Init(GPIOD, &GPIO_InitStruct);

  /*Configure GPIO pin : OTG_FS_OverCurrent_Pin */
  GPIO_InitStruct.Pin = OTG_FS_OverCurrent_Pin;
  GPIO_InitStruct.Mode = GPIO_MODE_INPUT;
  GPIO_InitStruct.Pull = GPIO_NOPULL;
  HAL_GPIO_Init(OTG_FS_OverCurrent_GPIO_Port, &GPIO_InitStruct);

  /*Configure GPIO pin : MEMS_INT2_Pin */
  GPIO_InitStruct.Pin = MEMS_INT2_Pin;
  GPIO_InitStruct.Mode = GPIO_MODE_EVT_RISING;
  GPIO_InitStruct.Pull = GPIO_NOPULL;
  HAL_GPIO_Init(MEMS_INT2_GPIO_Port, &GPIO_InitStruct);

  /*Configure GPIO pin: Pin 8 & Pin 9*/
  GPIO_InitStruct.Pin = GPIO_PIN_8 | GPIO_PIN_9;
  GPIO_InitStruct.Mode = GPIO_MODE_AF_PP;
  GPIO_InitStruct.Pull = GPIO_PULLUP;
  GPIO_InitStruct.Speed = GPIO_SPEED_FREQ_VERY_HIGH;
  GPIO_InitStruct.Alternate = GPIO_AF7_USART3;
  HAL_GPIO_Init(GPIOD, &GPIO_InitStruct);

  /* USER CODE BEGIN MX_GPIO_Init_2 */

  /* USER CODE END MX_GPIO_Init_2 */
}


static void MX_USART3_UART_Init(void)
{
  /* USER CODE BEGIN MX_USART3_UART_Init */
	__HAL_RCC_USART3_CLK_ENABLE();

  huart3.Instance = USART3;
  huart3.Init.BaudRate = 115200;
  huart3.Init.WordLength = UART_WORDLENGTH_8B;
  huart3.Init.StopBits = UART_STOPBITS_1;
  huart3.Init.Parity = UART_PARITY_NONE;
  huart3.Init.Mode = UART_MODE_TX_RX;
  huart3.Init.HwFlowCtl = UART_HWCONTROL_NONE;
  huart3.Init.OverSampling = UART_OVERSAMPLING_16;

  if (HAL_UART_Init(&huart3) != HAL_OK)
  {
    Error_Handler();
  }
}

/* USER CODE BEGIN 4 */

/* USER CODE END 4 */

/**
  * @brief  This function is executed in case of error occurrence.
  * @retval None
  */
void Error_Handler(void)
{
  /* USER CODE BEGIN Error_Handler_Debug */
  /* User can add his own implementation to report the HAL error return state */
  __disable_irq();
  while (1)
  {
  }
  /* USER CODE END Error_Handler_Debug */
}
#ifdef USE_FULL_ASSERT
/**
  * @brief  Reports the name of the source file and the source line number
  *         where the assert_param error has occurred.
  * @param  file: pointer to the source file name
  * @param  line: assert_param error line source number
  * @retval None
  */
void assert_failed(uint8_t *file, uint32_t line)
{
  /* USER CODE BEGIN 6 */
  /* User can add his own implementation to report the file name and line number,
     ex: printf("Wrong parameters value: file %s on line %d\r\n", file, line) */
  /* USER CODE END 6 */
}
#endif /* USE_FULL_ASSERT */

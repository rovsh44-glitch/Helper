import { helperApi } from './generatedApiClient';

export type {
  ProviderDoctorCheckDto,
  ProviderDoctorProfileReportDto,
  ProviderDoctorReportDto,
  ProviderDoctorRunRequestDto,
} from './generatedApiClient';

export async function runProviderDoctor(body?: Parameters<typeof helperApi.runRuntimeDoctor>[0]) {
  return helperApi.runRuntimeDoctor(body);
}

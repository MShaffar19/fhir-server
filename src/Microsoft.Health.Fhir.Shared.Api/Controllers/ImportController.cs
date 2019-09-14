﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Net;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Import.Models;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Messages.Import;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    public class ImportController : Controller
    {
        /*
         * We are currently hardcoding the routing attribute to be specific to Export and
         * get forwarded to this controller. As we add more operations we would like to resolve
         * the routes in a more dynamic manner. One way would be to use a regex route constraint
         * - eg: "{operation:regex(^\\$([[a-zA-Z]]+))}" - and use the appropriate operation handler.
         * Another way would be to use the capability statement to dynamically determine what operations
         * are supported.
         * It would be easier to determine what pattern to follow once we have built support for a couple
         * of operations. Then we can refactor this controller accordingly.
         */

        private readonly IMediator _mediator;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly IUrlResolver _urlResolver;
        private readonly ImportJobConfiguration _importJobConfig;
        private readonly ILogger<ExportController> _logger;

        public ImportController(
            IMediator mediator,
            IFhirRequestContextAccessor fhirRequestContextAccessor,
            IUrlResolver urlResolver,
            IOptions<OperationsConfiguration> operationsConfig,
            ILogger<ExportController> logger)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNull(operationsConfig?.Value?.Export, nameof(operationsConfig));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _mediator = mediator;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _urlResolver = urlResolver;
            _importJobConfig = operationsConfig.Value.Import;
            _logger = logger;
        }

        [HttpPost]
        [Route(KnownRoutes.Import)]
        [AuditEventType(AuditEventSubType.Import)]
        public async Task<IActionResult> Import([FromBody]ImportRequest importRequest)
        {
            if (!_importJobConfig.Enabled)
            {
                throw new RequestNotValidException(string.Format(Resources.UnsupportedOperation, OperationsConstants.Import));
            }

            CreateImportResponse response = await _mediator.ImportAsync(_fhirRequestContextAccessor.FhirRequestContext.Uri, importRequest, HttpContext.RequestAborted);

            var importResult = ImportResult.Accepted();
            importResult.SetContentLocationHeader(_urlResolver, RouteNames.GetImportStatusById, response.JobId);

            return importResult;
        }

        [HttpGet]
        [Route(KnownRoutes.ImportStatusById, Name = RouteNames.GetImportStatusById)]
        [AuditEventType(AuditEventSubType.Import)]
        public async Task<IActionResult> GetImportStatusById(string id)
        {
            var getImportResult = await _mediator.GetImportStatusAsync(_fhirRequestContextAccessor.FhirRequestContext.Uri, id);

            // If the job is complete, we need to return 200 along the completed data to the client.
            // Else we need to return 202.
            ImportResult importActionResult;
            if (getImportResult.StatusCode == HttpStatusCode.OK)
            {
                importActionResult = ImportResult.Ok(getImportResult.JobResult);
                importActionResult.SetContentTypeHeader(OperationsConstants.ExportContentTypeHeaderValue);
            }
            else
            {
                importActionResult = ImportResult.Accepted();
            }

            return importActionResult;
        }
    }
}
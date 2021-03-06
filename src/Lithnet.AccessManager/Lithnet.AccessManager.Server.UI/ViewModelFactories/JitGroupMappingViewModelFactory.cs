﻿using Lithnet.AccessManager.Server.Configuration;
using Stylet;

namespace Lithnet.AccessManager.Server.UI
{
    public class JitGroupMappingViewModelFactory : IJitGroupMappingViewModelFactory
    {
        private readonly IModelValidator<JitGroupMappingViewModel> validator;

        public JitGroupMappingViewModelFactory(IModelValidator<JitGroupMappingViewModel> validator)
        {
            this.validator = validator;
        }

        public JitGroupMappingViewModel CreateViewModel(JitGroupMapping model)
        {
            return new JitGroupMappingViewModel(model, validator);
        }
    }
}

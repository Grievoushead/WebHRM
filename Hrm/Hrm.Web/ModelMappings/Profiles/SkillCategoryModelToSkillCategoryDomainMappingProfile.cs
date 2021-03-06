﻿using AutoMapper;
using Hrm.Data.EF.Models;
using Hrm.Web.Models.SkillCategory;
using Profile = AutoMapper.Profile;

namespace Hrm.Web.ModelMappings.Profiles
{
    public class SkillCategoryModelToSkillCategoryDomainMappingProfile : Profile
    {
        protected override void Configure()
        {
            Mapper.CreateMap<SkillCategoryModel, SkillCategory>().ReverseMap();
        }
    }
}